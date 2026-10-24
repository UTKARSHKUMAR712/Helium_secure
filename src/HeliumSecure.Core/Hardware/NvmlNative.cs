using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeliumSecure.Core.Hardware;

[StructLayout(LayoutKind.Sequential)]
internal struct NvmlUtilization
{
    public uint Gpu;
    public uint Memory;
}

internal static class NvmlNative
{
    private static bool? _isNvmlAvailable;
    private static readonly object _lock = new();

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
    private static extern int nvmlDeviceGetCount_v2(out uint count);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization rates);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int nvmlShutdown();

    public static bool TryGetMetrics(uint deviceIndex, out float? utilization, out float? temperature)
    {
        utilization = null;
        temperature = null;

        lock (_lock)
        {
            if (_isNvmlAvailable != false)
            {
                try
                {
                    int init = nvmlInit_v2();
                    if (init == 0)
                    {
                        _isNvmlAvailable = true;
                        try
                        {
                            if (nvmlDeviceGetCount_v2(out uint count) == 0 && deviceIndex < count)
                            {
                                if (nvmlDeviceGetHandleByIndex_v2(deviceIndex, out IntPtr device) == 0 && device != IntPtr.Zero)
                                {
                                    if (nvmlDeviceGetUtilizationRates(device, out var rates) == 0)
                                    {
                                        utilization = rates.Gpu;
                                    }

                                    if (nvmlDeviceGetTemperature(device, 0, out uint temp) == 0)
                                    {
                                        temperature = temp;
                                    }

                                    if (utilization.HasValue || temperature.HasValue)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            nvmlShutdown();
                        }
                    }
                    else
                    {
                        _isNvmlAvailable = false;
                    }
                }
                catch
                {
                    _isNvmlAvailable = false;
                }
            }
        }

        // Fallback: nvidia-smi CLI
        return TryGetMetricsFromNvSmi(deviceIndex, out utilization, out temperature);
    }

    private static bool TryGetMetricsFromNvSmi(uint deviceIndex, out float? utilization, out float? temperature)
    {
        utilization = null;
        temperature = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = $"--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits -i {deviceIndex}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Trim().Split(',');
                if (parts.Length >= 2)
                {
                    if (float.TryParse(parts[0].Trim(), out float u)) utilization = u;
                    if (float.TryParse(parts[1].Trim(), out float t)) temperature = t;
                    return utilization.HasValue || temperature.HasValue;
                }
            }
        }
        catch { }

        return false;
    }
}
