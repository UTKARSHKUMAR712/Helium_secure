namespace HeliumSecure.Core.Storage;

public sealed class ProfileStorageModel
{
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public bool IsLocked { get; set; }

    public long VaultSizeBytes { get; set; }
    public long BackupSizeBytes { get; set; }
    public long WorkingDirectorySizeBytes { get; set; }
    public long CrashRecoverySizeBytes { get; set; }
    public long TotalSizeBytes { get; set; }

    public bool IsMeasurementComplete { get; set; } = true;

    public string VaultSizeFormatted => StorageFormatHelper.FormatBytes(VaultSizeBytes);
    public string BackupSizeFormatted => StorageFormatHelper.FormatBytes(BackupSizeBytes);
    public string WorkingDirectoryFormatted => StorageFormatHelper.FormatBytes(WorkingDirectorySizeBytes);
    public string CrashRecoveryFormatted => StorageFormatHelper.FormatBytes(CrashRecoverySizeBytes);
    public string TotalSizeFormatted => StorageFormatHelper.FormatBytes(TotalSizeBytes);

    public string CreatedAtFormatted => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string LastUsedFormatted => LastUsedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
