namespace HeliumSecure.Core.Hardware;

public interface IGpuDetector
{
    IReadOnlyList<GpuAdapterInfo> DetectAdapters();
}

public interface ISystemMetricsProvider
{
    SystemMetrics GetCurrentMetrics();
}
