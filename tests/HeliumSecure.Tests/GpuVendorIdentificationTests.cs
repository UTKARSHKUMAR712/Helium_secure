using HeliumSecure.Core.Hardware;

namespace HeliumSecure.Tests;

public sealed class GpuVendorIdentificationTests
{
    private sealed class MockGpuDetector : IGpuDetector
    {
        private readonly List<GpuAdapterInfo> _adapters;

        public MockGpuDetector(params GpuAdapterInfo[] adapters)
        {
            _adapters = adapters.ToList();
        }

        public IReadOnlyList<GpuAdapterInfo> DetectAdapters() => _adapters.AsReadOnly();
    }

    [Fact]
    public void VendorResolution_FromPciVendorId_CorrectlyIdentifiesVendors()
    {
        // 0x10DE -> NVIDIA (N)
        Assert.Equal(GpuVendor.Nvidia, GpuVendorHelper.FromPciVendorId(0x10DE));
        Assert.Equal("N", GpuVendor.Nvidia.ToPrefix());

        // 0x8086 -> Intel (I)
        Assert.Equal(GpuVendor.Intel, GpuVendorHelper.FromPciVendorId(0x8086));
        Assert.Equal("I", GpuVendor.Intel.ToPrefix());

        // 0x1002 / 0x1022 -> AMD (A)
        Assert.Equal(GpuVendor.Amd, GpuVendorHelper.FromPciVendorId(0x1002));
        Assert.Equal(GpuVendor.Amd, GpuVendorHelper.FromPciVendorId(0x1022));
        Assert.Equal("A", GpuVendor.Amd.ToPrefix());

        // Unknown
        Assert.Equal(GpuVendor.Unknown, GpuVendorHelper.FromPciVendorId(0x9999));
        Assert.Equal("?", GpuVendor.Unknown.ToPrefix());
    }

    [Fact]
    public void VendorResolution_FromNameFallback_CorrectlyIdentifiesVendors()
    {
        Assert.Equal(GpuVendor.Nvidia, GpuVendorHelper.FromName("NVIDIA GeForce RTX 4070 Laptop GPU"));
        Assert.Equal(GpuVendor.Intel, GpuVendorHelper.FromName("Intel(R) Iris(R) Xe Graphics"));
        Assert.Equal(GpuVendor.Amd, GpuVendorHelper.FromName("AMD Radeon RX 7900 XTX"));
        Assert.Equal(GpuVendor.Unknown, GpuVendorHelper.FromName("Microsoft Basic Render Driver"));
    }

    [Fact]
    public void NvidiaOnly_DisplaysExactExpectedFormat()
    {
        // Example: CPU 18% | RAM 12.4/24 GB (52%) | N GPU 34% · 58°C
        var metrics = new SystemMetrics
        {
            CpuPercentage = 18f,
            UsedRamGb = 12.4,
            TotalRamGb = 24.0,
            RamPercentage = 52,
            GpuAdapters = new List<GpuAdapterInfo>
            {
                new GpuAdapterInfo("NVIDIA GeForce RTX 4070", 0x10DE, 0x2860, utilization: 34f, temperature: 58f)
            }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 18% | RAM 12.4/24 GB (52%) | N GPU 34% · 58°C", overlay);
    }

    [Fact]
    public void IntelIntegrated_DisplaysExactExpectedFormat()
    {
        // Example: CPU 12% | RAM 8.2/24 GB (34%) | I GPU 7% · 46°C
        var metrics = new SystemMetrics
        {
            CpuPercentage = 12f,
            UsedRamGb = 8.2,
            TotalRamGb = 24.0,
            RamPercentage = 34,
            GpuAdapters = new List<GpuAdapterInfo>
            {
                new GpuAdapterInfo("Intel(R) Iris(R) Xe Graphics", 0x8086, 0x9A49, utilization: 7f, temperature: 46f)
            }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 12% | RAM 8.2/24 GB (34%) | I GPU 7% · 46°C", overlay);
    }

    [Fact]
    public void AmdGraphics_DisplaysExactExpectedFormat()
    {
        // Example: CPU 21% | RAM 10.1/24 GB (42%) | A GPU 28% · 61°C
        var metrics = new SystemMetrics
        {
            CpuPercentage = 21f,
            UsedRamGb = 10.1,
            TotalRamGb = 24.0,
            RamPercentage = 42,
            GpuAdapters = new List<GpuAdapterInfo>
            {
                new GpuAdapterInfo("AMD Radeon RX 6800M", 0x1002, 0x73DF, utilization: 28f, temperature: 61f)
            }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 21% | RAM 10.1/24 GB (42%) | A GPU 28% · 61°C", overlay);
    }

    [Fact]
    public void HybridGpu_IntelIntegratedAndNvidiaDiscrete_DisplaysBothCorrectly()
    {
        // Example: CPU 12% | RAM 8.2/24 GB (34%) | I GPU 8% | N GPU 34% · 58°C
        var metrics = new SystemMetrics
        {
            CpuPercentage = 12f,
            UsedRamGb = 8.2,
            TotalRamGb = 24.0,
            RamPercentage = 34,
            GpuAdapters = new List<GpuAdapterInfo>
            {
                new GpuAdapterInfo("Intel(R) Graphics", 0x8086, 0xA7AB, utilization: 8f, temperature: null),
                new GpuAdapterInfo("NVIDIA GeForce RTX 5050 Laptop GPU", 0x10DE, 0x2D98, utilization: 34f, temperature: 58f)
            }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 12% | RAM 8.2/24 GB (34%) | I GPU 8% | N GPU 34% · 58°C", overlay);
    }

    [Fact]
    public void HybridGpu_AmdIntegratedAndNvidiaDiscrete_DisplaysBothCorrectly()
    {
        var metrics = new SystemMetrics
        {
            CpuPercentage = 15f,
            UsedRamGb = 9.0,
            TotalRamGb = 32.0,
            RamPercentage = 28,
            GpuAdapters = new List<GpuAdapterInfo>
            {
                new GpuAdapterInfo("AMD Radeon(TM) 680M", 0x1002, 0x1681, utilization: 14f, temperature: 48f),
                new GpuAdapterInfo("NVIDIA GeForce RTX 4080 Laptop GPU", 0x10DE, 0x27E0, utilization: 55f, temperature: 65f)
            }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 15% | RAM 9.0/32 GB (28%) | A GPU 14% · 48°C | N GPU 55% · 65°C", overlay);
    }

    [Fact]
    public void UnavailableMetrics_ShowsVendorAndEmDashWithoutFabrication()
    {
        // NVIDIA with unavailable metrics (Requirement 6: e.g. "N GPU —")
        var nvGpu = new GpuAdapterInfo("NVIDIA GeForce RTX 3060", 0x10DE, 0x2503, utilization: null, temperature: null);
        Assert.Equal("N GPU —", nvGpu.FormatGpuMetric());

        // Intel with unavailable metrics
        var intelGpu = new GpuAdapterInfo("Intel UHD Graphics 630", 0x8086, 0x3E92, utilization: null, temperature: null);
        Assert.Equal("I GPU —", intelGpu.FormatGpuMetric());

        // AMD with unavailable metrics
        var amdGpu = new GpuAdapterInfo("AMD Radeon Vega 8", 0x1002, 0x15DD, utilization: null, temperature: null);
        Assert.Equal("A GPU —", amdGpu.FormatGpuMetric());

        // Hybrid with one active and one unavailable (e.g. discrete GPU sleeping)
        var metrics = new SystemMetrics
        {
            CpuPercentage = 10f,
            UsedRamGb = 6.5,
            TotalRamGb = 16.0,
            RamPercentage = 41,
            GpuAdapters = new List<GpuAdapterInfo> { intelGpu, nvGpu }
        };

        string overlay = metrics.FormatOverlayString();
        Assert.Equal("CPU 10% | RAM 6.5/16 GB (41%) | I GPU — | N GPU —", overlay);
    }

    [Fact]
    public void StrictMetricIsolation_NeverMixesUtilizationOrTemperatureBetweenAdapters()
    {
        // Verify Requirement 5: Never show NVIDIA utilization beside Intel temperature or vice versa
        var intelGpu = new GpuAdapterInfo("Intel Graphics", 0x8086, 0xA7AB, utilization: 12f, temperature: 45f);
        var nvidiaGpu = new GpuAdapterInfo("NVIDIA RTX", 0x10DE, 0x2860, utilization: 75f, temperature: 72f);

        Assert.Equal("I GPU 12% · 45°C", intelGpu.FormatGpuMetric());
        Assert.Equal("N GPU 75% · 72°C", nvidiaGpu.FormatGpuMetric());

        // Ensure changing NVIDIA temperature does NOT alter Intel
        nvidiaGpu.TemperatureCelsius = 80f;
        Assert.Equal("I GPU 12% · 45°C", intelGpu.FormatGpuMetric());
        Assert.Equal("N GPU 75% · 80°C", nvidiaGpu.FormatGpuMetric());
    }

    [Fact]
    public void Tooltip_PreservesFullModelNameAndPciDetails()
    {
        // Long model name: overlay stays compact, tooltip contains full details
        string longName = "NVIDIA GeForce RTX 4090 Laptop GPU with Max-Q Design High Performance Edition";
        var gpu = new GpuAdapterInfo(longName, 0x10DE, 0x2717, utilization: 40f, temperature: 62f, vramBytes: 17179869184);

        // Overlay remains compact
        Assert.Equal("N GPU 40% · 62°C", gpu.FormatGpuMetric());

        // Tooltip contains full name, PCI vendor/device ID, and VRAM
        string tooltip = gpu.FormatTooltip();
        Assert.Contains(longName, tooltip);
        Assert.Contains("0x10DE:0x2717", tooltip);
        Assert.Contains("16,384 MB", tooltip);
    }

    [Fact]
    public void WindowsSystemMetricsProvider_WithMockDetector_PopulatesMetricsCorrectly()
    {
        var mockDetector = new MockGpuDetector(
            new GpuAdapterInfo("Mock NVIDIA", 0x10DE, 0x1234, utilization: 25f, temperature: 55f),
            new GpuAdapterInfo("Mock Intel", 0x8086, 0x5678, utilization: 10f, temperature: 40f)
        );

        var provider = new WindowsSystemMetricsProvider(mockDetector);
        var metrics = provider.GetCurrentMetrics();

        Assert.True(metrics.TotalRamGb > 0);
        Assert.Equal(2, metrics.GpuAdapters.Count);
        Assert.Equal(GpuVendor.Nvidia, metrics.GpuAdapters[0].Vendor);
        Assert.Equal(GpuVendor.Intel, metrics.GpuAdapters[1].Vendor);
    }

    [Fact]
    public void PhysicalMachine_DetectionAndMetrics_ExecuteWithoutExceptions()
    {
        var detector = new WindowsGpuDetector();
        var adapters = detector.DetectAdapters();

        // On physical Windows machine, should detect at least 1 adapter
        Assert.NotEmpty(adapters);
        foreach (var adapter in adapters)
        {
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            Assert.Contains(adapter.VendorPrefix, new[] { "N", "I", "A", "?" });
        }

        var provider = new WindowsSystemMetricsProvider(detector);
        var metrics = provider.GetCurrentMetrics();

        Assert.True(metrics.TotalRamGb > 0);
        string overlayString = metrics.FormatOverlayString();
        Assert.StartsWith("CPU ", overlayString);
        Assert.Contains("RAM ", overlayString);
    }
}
