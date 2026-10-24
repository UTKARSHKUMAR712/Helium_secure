using System.Text;

namespace HeliumSecure.Core.Hardware;

public sealed class SystemMetrics
{
    public float CpuPercentage { get; set; }
    public double UsedRamGb { get; set; }
    public double TotalRamGb { get; set; }
    public int RamPercentage { get; set; }
    public List<GpuAdapterInfo> GpuAdapters { get; set; } = new();

    public string FormatCpuRam()
    {
        string totalRamStr = (TotalRamGb > 0 && Math.Abs(TotalRamGb % 1) < 0.05)
            ? $"{(int)Math.Round(TotalRamGb)}"
            : $"{TotalRamGb:F1}";

        return $"CPU {Math.Round(CpuPercentage)}% | RAM {UsedRamGb:F1}/{totalRamStr} GB ({RamPercentage}%)";
    }

    /// <summary>
    /// Formats the complete overlay text string:
    /// e.g. "CPU 18% | RAM 12.4/24 GB (52%) | N GPU 34% · 58°C"
    /// or for hybrid GPUs:
    /// "CPU 12% | RAM 8.2/24 GB (34%) | I GPU 8% | N GPU 34% · 58°C"
    /// </summary>
    public string FormatOverlayString()
    {
        var sb = new StringBuilder();
        sb.Append(FormatCpuRam());

        if (GpuAdapters.Count > 0)
        {
            foreach (var gpu in GpuAdapters)
            {
                sb.Append(" | ");
                sb.Append(gpu.FormatGpuMetric());
            }
        }
        else
        {
            sb.Append(" | GPU —");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates detailed multi-line tooltip text.
    /// </summary>
    public string FormatTooltip()
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormatOverlayString());
        sb.AppendLine();
        sb.AppendLine($"CPU Utilization: {Math.Round(CpuPercentage)}%");
        sb.AppendLine($"RAM Footprint: {UsedRamGb:F2} GB of {TotalRamGb:F2} GB used ({RamPercentage}%)");

        if (GpuAdapters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Detected GPU Adapters:");
            for (int i = 0; i < GpuAdapters.Count; i++)
            {
                sb.AppendLine($"  • [{GpuAdapters[i].VendorPrefix}] {GpuAdapters[i].FormatTooltip()}");
            }
        }
        else
        {
            sb.AppendLine("No dedicated or integrated GPU detected.");
        }

        return sb.ToString().TrimEnd();
    }

    public override string ToString() => FormatOverlayString();
}
