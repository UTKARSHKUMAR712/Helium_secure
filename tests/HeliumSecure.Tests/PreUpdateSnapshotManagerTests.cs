using System;
using System.IO;
using HeliumSecure.Core.Storage;
using Xunit;

namespace HeliumSecure.Tests;

public class PreUpdateSnapshotManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _vaultsDir;
    private readonly string _manifestPath;
    private readonly PreUpdateSnapshotManager _manager;

    public PreUpdateSnapshotManagerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HeliumTest_Snapshots_" + Guid.NewGuid().ToString("N"));
        _vaultsDir = Path.Combine(_testRoot, "Vaults");
        _manifestPath = Path.Combine(_testRoot, "manifest.json");

        Directory.CreateDirectory(_vaultsDir);
        File.WriteAllText(_manifestPath, "{\"profiles\":[]}");

        _manager = new PreUpdateSnapshotManager(_vaultsDir, _manifestPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void CreateSnapshot_CopiesVaultFilesAndManifest_WithValidMetadata()
    {
        // Arrange
        var vault1 = Path.Combine(_vaultsDir, "profile1.hsv");
        var vault2 = Path.Combine(_vaultsDir, "profile2.hsv.bak");
        File.WriteAllBytes(vault1, new byte[] { 1, 2, 3, 4, 5 });
        File.WriteAllBytes(vault2, new byte[] { 6, 7, 8 });

        // Act
        var meta = _manager.CreatePreUpdateSnapshot(
            sourceVersion: "0.20.1.1",
            targetVersion: "0.21.1.1",
            updateType: "BrowserPayloadUpdate");

        // Assert
        Assert.NotNull(meta);
        Assert.NotEmpty(meta.SnapshotId);
        Assert.Equal("0.20.1.1", meta.SourceVersion);
        Assert.Equal("0.21.1.1", meta.TargetVersion);
        Assert.Equal("BrowserPayloadUpdate", meta.UpdateType);
        Assert.Contains("profile1.hsv", meta.VaultFiles);
        Assert.Contains("profile2.hsv.bak", meta.VaultFiles);
        Assert.True(meta.TotalSizeBytes > 0);

        var latest = _manager.GetLatestSnapshot();
        Assert.NotNull(latest);
        Assert.Equal(meta.SnapshotId, latest.SnapshotId);
    }

    [Fact]
    public void RollbackSnapshot_RestoresOriginalVaultContent_WhenModified()
    {
        // Arrange
        var vaultFile = Path.Combine(_vaultsDir, "profile.hsv");
        File.WriteAllBytes(vaultFile, new byte[] { 10, 20, 30 });
        File.WriteAllText(_manifestPath, "{\"version\":1}");

        var meta = _manager.CreatePreUpdateSnapshot("1.0.4", "1.0.5");

        // Simulate update corruption or modification
        File.WriteAllBytes(vaultFile, new byte[] { 99, 99 });
        File.WriteAllText(_manifestPath, "{\"version\":2, \"corrupt\": true}");

        // Act
        bool rolledBack = _manager.RollbackLatestSnapshot();

        // Assert
        Assert.True(rolledBack);
        Assert.Equal(new byte[] { 10, 20, 30 }, File.ReadAllBytes(vaultFile));
        Assert.Equal("{\"version\":1}", File.ReadAllText(_manifestPath));
    }

    [Fact]
    public void DeleteSnapshot_RemovesSnapshotFiles()
    {
        // Arrange
        var meta = _manager.CreatePreUpdateSnapshot("1.0.4", "1.0.5");
        Assert.NotNull(_manager.GetLatestSnapshot());

        // Act
        bool deleted = _manager.DeleteSnapshot(meta.SnapshotId);

        // Assert
        Assert.True(deleted);
        Assert.Null(_manager.GetLatestSnapshot());
    }

    [Fact]
    public void CleanupSnapshotsOnSystemRestart_DeletesPriorBootSnapshots()
    {
        // Arrange
        var meta = _manager.CreatePreUpdateSnapshot("1.0.4", "1.0.5");
        Assert.NotNull(_manager.GetLatestSnapshot());

        // Simulate a system restart where current boot time is 1 hour after the snapshot's boot time
        var simulatedNewBootTime = meta.SystemBootTimeUtc.AddHours(1);

        // Act
        int deletedCount = _manager.CleanupSnapshotsOnSystemRestart(simulatedNewBootTime);

        // Assert
        Assert.Equal(1, deletedCount);
        Assert.Null(_manager.GetLatestSnapshot());
    }

    [Fact]
    public void CleanupSnapshotsOnSystemRestart_KeepsCurrentBootSnapshots()
    {
        // Arrange
        var meta = _manager.CreatePreUpdateSnapshot("1.0.4", "1.0.5");
        Assert.NotNull(_manager.GetLatestSnapshot());

        // Simulate same boot session (boot time is within 10 seconds of snapshot's recorded boot time)
        var simulatedSameBootTime = meta.SystemBootTimeUtc.AddSeconds(10);

        // Act
        int deletedCount = _manager.CleanupSnapshotsOnSystemRestart(simulatedSameBootTime);

        // Assert
        Assert.Equal(0, deletedCount);
        Assert.NotNull(_manager.GetLatestSnapshot());
    }
}
