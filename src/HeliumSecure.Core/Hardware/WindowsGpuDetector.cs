using System.Management;
using System.Text.RegularExpressions;

namespace HeliumSecure.Core.Hardware;

public sealed class WindowsGpuDetector : IGpuDetector
{
    public IReadOnlyList<GpuAdapterInfo> DetectAdapters()
    {
        var adapters = new List<GpuAdapterInfo>();

        // 1. Try DXGI enumeration
        try
        {
            var dxgiAdapters = DxgiNative.EnumerateAdapters();
            if (dxgiAdapters.Count > 0)
            {
                adapters.AddRange(dxgiAdapters);
            }
        }
        catch { }

        // 2. Query WMI Win32_VideoController
        try
        {
            var wmiAdapters = QueryWmiAdapters();

            if (adapters.Count == 0)
            {
                // Fallback to WMI if DXGI found nothing
                adapters.AddRange(wmiAdapters);
            }
            else
            {
                // Enrich DXGI adapters with WMI data (e.g. full device name, PNPDeviceID)
                foreach (var dxgi in adapters)
                {
                    var match = wmiAdapters.FirstOrDefault(w =>
                        (w.VendorId == dxgi.VendorId && w.DeviceId == dxgi.DeviceId) ||
                        (w.VendorId == dxgi.VendorId && w.Name.Contains(dxgi.Name, StringComparison.OrdinalIgnoreCase)) ||
                        (dxgi.Name.Contains(w.Name, StringComparison.OrdinalIgnoreCase)));

                    if (match != null)
                    {
                        if (string.IsNullOrWhiteSpace(dxgi.Name) || dxgi.Name.Length < match.Name.Length)
                        {
                            dxgi.Name = match.Name;
                        }
                        dxgi.PnpDeviceId = match.PnpDeviceId;
                        if (dxgi.DedicatedVideoMemoryBytes == 0 && match.DedicatedVideoMemoryBytes > 0)
                        {
                            dxgi.DedicatedVideoMemoryBytes = match.DedicatedVideoMemoryBytes;
                        }
                    }
                }

                // If WMI detected an adapter that DXGI missed, include it
                foreach (var wmi in wmiAdapters)
                {
                    if (!adapters.Any(a => a.VendorId == wmi.VendorId && a.DeviceId == wmi.DeviceId))
                    {
                        adapters.Add(wmi);
                    }
                }
            }
        }
        catch { }

        // Filter and assign indices
        for (int i = 0; i < adapters.Count; i++)
        {
            adapters[i].AdapterIndex = i;
            if (adapters[i].Vendor == GpuVendor.Unknown)
            {
                adapters[i].Vendor = GpuVendorHelper.FromPciVendorId(adapters[i].VendorId);
                if (adapters[i].Vendor == GpuVendor.Unknown)
                {
                    adapters[i].Vendor = GpuVendorHelper.FromName(adapters[i].Name);
                }
            }
        }

        return adapters.AsReadOnly();
    }

    private static List<GpuAdapterInfo> QueryWmiAdapters()
    {
        var list = new List<GpuAdapterInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, AdapterRAM, Status FROM Win32_VideoController");

            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                string name = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                string pnp = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) ||
                    name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                uint vendorId = 0;
                uint deviceId = 0;

                var venMatch = Regex.Match(pnp, @"VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                if (venMatch.Success)
                {
                    vendorId = Convert.ToUInt32(venMatch.Groups[1].Value, 16);
                }

                var devMatch = Regex.Match(pnp, @"DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                if (devMatch.Success)
                {
                    deviceId = Convert.ToUInt32(devMatch.Groups[1].Value, 16);
                }

                ulong vram = 0;
                if (obj["AdapterRAM"] != null)
                {
                    ulong.TryParse(obj["AdapterRAM"].ToString(), out vram);
                }

                var info = new GpuAdapterInfo
                {
                    Name = name,
                    VendorId = vendorId,
                    DeviceId = deviceId,
                    Vendor = GpuVendorHelper.FromPciVendorId(vendorId),
                    DedicatedVideoMemoryBytes = vram,
                    PnpDeviceId = pnp
                };

                if (info.Vendor == GpuVendor.Unknown)
                {
                    info.Vendor = GpuVendorHelper.FromName(name);
                }

                list.Add(info);
            }
        }
        catch { }

        return list;
    }
}
