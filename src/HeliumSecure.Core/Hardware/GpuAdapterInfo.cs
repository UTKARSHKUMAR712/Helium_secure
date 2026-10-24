namespace HeliumSecure.Core.Hardware;

public sealed class GpuAdapterInfo
{
    public string Name { get; set; } = string.Empty;
    public uint VendorId { get; set; }
    public uint DeviceId { get; set; }
    public GpuVendor Vendor { get; set; }
    public string VendorPrefix => Vendor.ToPrefix();
    public ulong DedicatedVideoMemoryBytes { get; set; }
    public int AdapterIndex { get; set; }
    public string? PnpDeviceId { get; set; }
    public ulong AdapterLuid { get; set; }

    public float? UtilizationPercentage { get; set; }
    public float? TemperatureCelsius { get; set; }

    public bool HasMetrics => UtilizationPercentage.HasValue || TemperatureCelsius.HasValue;

    public GpuAdapterInfo()
    {
    }

    public GpuAdapterInfo(
        string name,
        uint vendorId,
        uint deviceId = 0,
        float? utilization = null,
        float? temperature = null,
        ulong vramBytes = 0)
    {
        Name = name;
        VendorId = vendorId;
        DeviceId = deviceId;
        Vendor = GpuVendorHelper.FromPciVendorId(vendorId);
        if (Vendor == GpuVendor.Unknown)
        {
            Vendor = GpuVendorHelper.FromName(name);
        }
        UtilizationPercentage = utilization;
        TemperatureCelsius = temperature;
        DedicatedVideoMemoryBytes = vramBytes;
    }

    /// <summary>
    /// Formats the GPU metric display according to requirements:
    /// e.g. "N GPU 34% · 58°C", "I GPU 7% · 46°C", "A GPU 28% · 61°C", or "N GPU —".
    /// Never pairs an adapter's utilization with another adapter's temperature.
    /// </summary>
    public string FormatGpuMetric()
    {
        string prefix = VendorPrefix;

        if (UtilizationPercentage.HasValue && TemperatureCelsius.HasValue)
        {
            return $"{prefix} GPU {Math.Round(UtilizationPercentage.Value)}% · {Math.Round(TemperatureCelsius.Value)}°C";
        }

        if (UtilizationPercentage.HasValue)
        {
            return $"{prefix} GPU {Math.Round(UtilizationPercentage.Value)}%";
        }

        if (TemperatureCelsius.HasValue)
        {
            return $"{prefix} GPU · {Math.Round(TemperatureCelsius.Value)}°C";
        }

        // Metrics unavailable (Requirement 6)
        return $"{prefix} GPU —";
    }

    /// <summary>
    /// Generates detailed tooltip including full model name, PCI ID, and memory.
    /// </summary>
    public string FormatTooltip()
    {
        string pci = VendorId > 0
            ? $" [PCI 0x{VendorId:X4}:0x{DeviceId:X4}]"
            : "";
        string vram = DedicatedVideoMemoryBytes > 0
            ? $", VRAM: {DedicatedVideoMemoryBytes / (1024 * 1024):N0} MB"
            : "";
        string metrics = HasMetrics
            ? $", Util: {(UtilizationPercentage.HasValue ? $"{Math.Round(UtilizationPercentage.Value)}%" : "N/A")}, Temp: {(TemperatureCelsius.HasValue ? $"{Math.Round(TemperatureCelsius.Value)}°C" : "N/A")}"
            : ", Metrics: unavailable";

        return $"{Name}{pci}{vram}{metrics}";
    }

    public override string ToString() => FormatGpuMetric();
}
