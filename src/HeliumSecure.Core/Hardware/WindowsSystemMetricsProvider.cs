using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace HeliumSecure.Core.Hardware;

public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    private readonly IGpuDetector _gpuDetector;
    private List<GpuAdapterInfo>? _cachedAdapters;
    private DateTimeOffset _lastAdapterDetection = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    // CPU calculation state
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private bool _hasPrevCpuTime;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public WindowsSystemMetricsProvider(IGpuDetector? gpuDetector = null)
    {
        _gpuDetector = gpuDetector ?? new WindowsGpuDetector();
        // Prime CPU times
        GetCpuUtilization();
    }

    public SystemMetrics GetCurrentMetrics()
    {
        var metrics = new SystemMetrics();

        // 1. CPU
        metrics.CpuPercentage = GetCpuUtilization();

        // 2. RAM
        GetMemoryMetrics(out double usedGb, out double totalGb, out int ramPercent);
        metrics.UsedRamGb = usedGb;
        metrics.TotalRamGb = totalGb;
        metrics.RamPercentage = ramPercent;

        // 3. GPU Adapters
        var adapters = GetAdapters();
        int nvidiaIndex = 0;

        foreach (var src in adapters)
        {
            // Clone adapter info so each reading is fresh and thread-safe
            var adapter = new GpuAdapterInfo
            {
                Name = src.Name,
                VendorId = src.VendorId,
                DeviceId = src.DeviceId,
                Vendor = src.Vendor,
                DedicatedVideoMemoryBytes = src.DedicatedVideoMemoryBytes,
                AdapterIndex = src.AdapterIndex,
                PnpDeviceId = src.PnpDeviceId,
                AdapterLuid = src.AdapterLuid
            };

            // Query metrics for this adapter
            if (adapter.Vendor == GpuVendor.Nvidia)
            {
                if (NvmlNative.TryGetMetrics((uint)nvidiaIndex, out var util, out var temp))
                {
                    adapter.UtilizationPercentage = util;
                    adapter.TemperatureCelsius = temp;
                }
                nvidiaIndex++;
            }
            else
            {
                // For Intel / AMD: attempt Windows Performance Counter for GPU Engine if available
                TryGetGpuEngineUtilization(adapter);
            }

            metrics.GpuAdapters.Add(adapter);
        }

        return metrics;
    }

    private List<GpuAdapterInfo> GetAdapters()
    {
        lock (_lock)
        {
            if (_cachedAdapters == null || DateTimeOffset.UtcNow - _lastAdapterDetection > TimeSpan.FromMinutes(5))
            {
                _cachedAdapters = _gpuDetector.DetectAdapters().ToList();
                _lastAdapterDetection = DateTimeOffset.UtcNow;
            }
            return _cachedAdapters;
        }
    }

    private float GetCpuUtilization()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return 0f;
        }

        ulong idle = ToUlong(idleTime);
        ulong kernel = ToUlong(kernelTime);
        ulong user = ToUlong(userTime);

        if (!_hasPrevCpuTime)
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _hasPrevCpuTime = true;
            return 0f;
        }

        ulong deltaIdle = idle - _prevIdleTime;
        ulong deltaKernel = kernel - _prevKernelTime;
        ulong deltaUser = user - _prevUserTime;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        ulong totalSys = deltaKernel + deltaUser;
        if (totalSys == 0) return 0f;

        float percent = (float)((totalSys - deltaIdle) * 100.0 / totalSys);
        return Math.Clamp(percent, 0f, 100f);
    }

    private static void GetMemoryMetrics(out double usedGb, out double totalGb, out int ramPercent)
    {
        usedGb = 0;
        totalGb = 0;
        ramPercent = 0;

        var mem = new MEMORYSTATUSEX();
        mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

        if (GlobalMemoryStatusEx(ref mem))
        {
            double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            totalGb = mem.ullTotalPhys / bytesPerGb;
            double availGb = mem.ullAvailPhys / bytesPerGb;
            usedGb = Math.Max(0, totalGb - availGb);
            ramPercent = (int)mem.dwMemoryLoad;
        }
    }

    private static void TryGetGpuEngineUtilization(GpuAdapterInfo adapter)
    {
        // Try reading GPU Engine utilization by LUID if available
        if (adapter.AdapterLuid == 0) return;

        try
        {
            string luidHex = $"0x{adapter.AdapterLuid:X16}";
            // Windows category: GPU Engine
            if (!PerformanceCounterCategory.Exists("GPU Engine")) return;

            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames();

            float total3DUtil = 0f;
            bool foundAny = false;

            foreach (var instance in instances)
            {
                if (instance.Contains(luidHex, StringComparison.OrdinalIgnoreCase) &&
                    instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    using var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    float val = counter.NextValue();
                    total3DUtil += val;
                    foundAny = true;
                }
            }

            if (foundAny)
            {
                adapter.UtilizationPercentage = Math.Clamp(total3DUtil, 0f, 100f);
            }
        }
        catch
        {
            // If performance counters are unavailable or access restricted, do not fabricate
        }
    }

    private static ulong ToUlong(FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }
}
