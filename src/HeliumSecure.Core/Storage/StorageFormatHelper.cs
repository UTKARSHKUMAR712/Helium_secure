namespace HeliumSecure.Core.Storage;

public static class StorageFormatHelper
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes == 0) return "0 B";

        int mag = (int)Math.Log(bytes, 1024);
        if (mag >= SizeSuffixes.Length) mag = SizeSuffixes.Length - 1;

        decimal adjustedSize = (decimal)bytes / (1L << (mag * 10));

        if (mag == 0)
        {
            return $"{bytes} B";
        }

        return $"{adjustedSize:N2} {SizeSuffixes[mag]}";
    }
}
