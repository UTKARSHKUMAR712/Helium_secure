namespace HeliumSecure.Core.Storage;

public sealed class StorageOverviewModel
{
    public long InstalledAppSizeBytes { get; set; }
    public long BundledBrowserSizeBytes { get; set; }
    public long LauncherSizeBytes { get; set; }

    public long TotalProfilesSizeBytes { get; set; }
    public long TotalVaultsAndBackupsSizeBytes { get; set; }
    public long TempAndCrashRecoverySizeBytes { get; set; }

    public long TotalFootprintBytes { get; set; }

    public bool IsMeasurementComplete { get; set; } = true;
    public string? MeasurementErrorMessage { get; set; }

    public string InstalledAppFormatted => StorageFormatHelper.FormatBytes(InstalledAppSizeBytes);
    public string BundledBrowserFormatted => StorageFormatHelper.FormatBytes(BundledBrowserSizeBytes);
    public string LauncherFormatted => StorageFormatHelper.FormatBytes(LauncherSizeBytes);
    public string TotalProfilesFormatted => StorageFormatHelper.FormatBytes(TotalProfilesSizeBytes);
    public string TotalVaultsAndBackupsFormatted => StorageFormatHelper.FormatBytes(TotalVaultsAndBackupsSizeBytes);
    public string TempAndCrashRecoveryFormatted => StorageFormatHelper.FormatBytes(TempAndCrashRecoverySizeBytes);
    public string TotalFootprintFormatted => StorageFormatHelper.FormatBytes(TotalFootprintBytes);
}
