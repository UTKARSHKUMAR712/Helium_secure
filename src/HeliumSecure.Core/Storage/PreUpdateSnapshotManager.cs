using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Core.Storage;

public sealed class PreUpdateSnapshotMeta
{
    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("systemBootTimeUtc")]
    public DateTime SystemBootTimeUtc { get; set; }

    [JsonPropertyName("sourceVersion")]
    public string? SourceVersion { get; set; }

    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; set; }

    [JsonPropertyName("vaultFiles")]
    public List<string> VaultFiles { get; set; } = new();

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("updateType")]
    public string UpdateType { get; set; } = "PayloadUpdate";
}

public sealed class PreUpdateSnapshotManager
{
    private readonly string _vaultsDir;
    private readonly string _manifestPath;
    private readonly string _snapshotsDir;
    private static readonly object _lock = new();

    public PreUpdateSnapshotManager(string vaultsDir, string manifestPath)
    {
        _vaultsDir = vaultsDir;
        _manifestPath = manifestPath;
        _snapshotsDir = Path.Combine(vaultsDir, "snapshots");
        Directory.CreateDirectory(_snapshotsDir);
    }

    public static DateTime GetCurrentSystemBootTimeUtc()
    {
        try
        {
            return DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Creates a complete pre-update encrypted snapshot of all .hsv vault files and manifest.
    /// </summary>
    public PreUpdateSnapshotMeta CreatePreUpdateSnapshot(string? sourceVersion = null, string? targetVersion = null, string updateType = "PayloadUpdate")
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_snapshotsDir);

            var snapshotId = $"payload_update_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
            var targetDir = Path.Combine(_snapshotsDir, snapshotId);
            Directory.CreateDirectory(targetDir);

            var meta = new PreUpdateSnapshotMeta
            {
                SnapshotId = snapshotId,
                CreatedAtUtc = DateTime.UtcNow,
                SystemBootTimeUtc = GetCurrentSystemBootTimeUtc(),
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                UpdateType = updateType
            };

            long totalBytes = 0;

            // 1. Snapshot all .hsv vault files
            if (Directory.Exists(_vaultsDir))
            {
                var vaultFiles = Directory.GetFiles(_vaultsDir, "*.hsv", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(_vaultsDir, "*.hsv.bak", SearchOption.TopDirectoryOnly));

                foreach (var file in vaultFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var dest = Path.Combine(targetDir, fileName);
                    File.Copy(file, dest, overwrite: true);
                    meta.VaultFiles.Add(fileName);
                    totalBytes += new FileInfo(dest).Length;
                }
            }

            // 2. Snapshot manifest.json
            if (File.Exists(_manifestPath))
            {
                var manifestDest = Path.Combine(targetDir, "manifest.json");
                File.Copy(_manifestPath, manifestDest, overwrite: true);
                totalBytes += new FileInfo(manifestDest).Length;
            }

            meta.TotalSizeBytes = totalBytes;

            // 3. Save snapshot metadata
            var metaPath = Path.Combine(targetDir, "snapshot_meta.json");
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);

            return meta;
        }
    }

    /// <summary>
    /// Gets the most recent pre-update snapshot, if one exists.
    /// </summary>
    public PreUpdateSnapshotMeta? GetLatestSnapshot()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_snapshotsDir)) return null;

            var dirs = Directory.GetDirectories(_snapshotsDir)
                .OrderByDescending(d => Directory.GetCreationTimeUtc(d));

            foreach (var dir in dirs)
            {
                var metaPath = Path.Combine(dir, "snapshot_meta.json");
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var json = File.ReadAllText(metaPath);
                        var meta = JsonSerializer.Deserialize<PreUpdateSnapshotMeta>(json);
                        if (meta != null) return meta;
                    }
                    catch { }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Restores the snapshot files back into the vaults directory for instant rollback.
    /// </summary>
    public bool RollbackLatestSnapshot()
    {
        lock (_lock)
        {
            var latest = GetLatestSnapshot();
            if (latest == null) return false;

            var snapshotDir = Path.Combine(_snapshotsDir, latest.SnapshotId);
            if (!Directory.Exists(snapshotDir)) return false;

            // 1. Restore all vault files
            foreach (var file in latest.VaultFiles)
            {
                var src = Path.Combine(snapshotDir, file);
                var dest = Path.Combine(_vaultsDir, file);
                if (File.Exists(src))
                {
                    File.Copy(src, dest, overwrite: true);
                }
            }

            // 2. Restore manifest
            var manifestSrc = Path.Combine(snapshotDir, "manifest.json");
            if (File.Exists(manifestSrc))
            {
                File.Copy(manifestSrc, _manifestPath, overwrite: true);
            }

            return true;
        }
    }

    /// <summary>
    /// Deletes a specific snapshot or the latest snapshot safely on demand.
    /// </summary>
    public bool DeleteSnapshot(string? snapshotId = null)
    {
        lock (_lock)
        {
            string? targetId = snapshotId ?? GetLatestSnapshot()?.SnapshotId;
            if (string.IsNullOrEmpty(targetId)) return false;

            var targetDir = Path.Combine(_snapshotsDir, targetId);
            if (Directory.Exists(targetDir))
            {
                VaultManager.CleanDirectorySafely(targetDir);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Deletes all existing pre-update snapshots on demand.
    /// </summary>
    public void DeleteAllSnapshots()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_snapshotsDir)) return;

            foreach (var dir in Directory.GetDirectories(_snapshotsDir))
            {
                VaultManager.CleanDirectorySafely(dir);
            }
        }
    }

    /// <summary>
    /// Scans for snapshots created in a prior boot session. If the system was restarted
    /// since the snapshot was taken, deletes the snapshot.
    /// </summary>
    public int CleanupSnapshotsOnSystemRestart(DateTime? simulatedCurrentBootTimeUtc = null)
    {
        lock (_lock)
        {
            if (!Directory.Exists(_snapshotsDir)) return 0;

            var currentBootTime = simulatedCurrentBootTimeUtc ?? GetCurrentSystemBootTimeUtc();
            int deletedCount = 0;

            foreach (var dir in Directory.GetDirectories(_snapshotsDir))
            {
                var metaPath = Path.Combine(dir, "snapshot_meta.json");
                if (!File.Exists(metaPath))
                {
                    VaultManager.CleanDirectorySafely(dir);
                    deletedCount++;
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<PreUpdateSnapshotMeta>(json);
                    if (meta != null)
                    {
                        // If system boot time is significantly later than when the snapshot was recorded,
                        // a system reboot has occurred!
                        if (currentBootTime > meta.SystemBootTimeUtc.AddMinutes(2))
                        {
                            VaultManager.CleanDirectorySafely(dir);
                            deletedCount++;
                        }
                    }
                    else
                    {
                        VaultManager.CleanDirectorySafely(dir);
                        deletedCount++;
                    }
                }
                catch
                {
                    VaultManager.CleanDirectorySafely(dir);
                    deletedCount++;
                }
            }

            return deletedCount;
        }
    }
}
