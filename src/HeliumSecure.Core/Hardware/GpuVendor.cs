namespace HeliumSecure.Core.Hardware;

public enum GpuVendor
{
    Unknown = 0,
    Nvidia,
    Intel,
    Amd
}

public static class GpuVendorHelper
{
    // Standard PCI Vendor IDs
    public const uint VendorIdNvidia = 0x10DE;
    public const uint VendorIdIntel = 0x8086;
    public const uint VendorIdAmd1 = 0x1002;
    public const uint VendorIdAmd2 = 0x1022;

    /// <summary>
    /// Resolves GPU vendor from PCI Vendor ID.
    /// NVIDIA: 0x10DE -> Nvidia
    /// Intel: 0x8086 -> Intel
    /// AMD: 0x1002 / 0x1022 -> Amd
    /// </summary>
    public static GpuVendor FromPciVendorId(uint vendorId)
    {
        return vendorId switch
        {
            VendorIdNvidia => GpuVendor.Nvidia,
            VendorIdIntel => GpuVendor.Intel,
            VendorIdAmd1 => GpuVendor.Amd,
            VendorIdAmd2 => GpuVendor.Amd,
            _ => GpuVendor.Unknown
        };
    }

    /// <summary>
    /// Fallback resolution from adapter name or description when PCI Vendor ID is missing.
    /// </summary>
    public static GpuVendor FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return GpuVendor.Unknown;

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tesla", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Nvidia;
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Intel;
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ATI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Amd;
        }

        return GpuVendor.Unknown;
    }

    /// <summary>
    /// Returns the required vendor prefix:
    /// 'N' = NVIDIA, 'I' = Intel, 'A' = AMD.
    /// </summary>
    public static string ToPrefix(this GpuVendor vendor)
    {
        return vendor switch
        {
            GpuVendor.Nvidia => "N",
            GpuVendor.Intel => "I",
            GpuVendor.Amd => "A",
            _ => "?"
        };
    }
}
