using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public class CrashRecoveryAndIntegrityTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public CrashRecoveryAndIntegrityTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_CrashTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
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
    public void InterruptedCommit_LeavesOriginalVaultValid()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string workingDir = Path.Combine(_testRoot, "working");
        string password = "StrongPassword123!";

        VaultManager.InitializeNewVault(vaultPath, profileId, password, _fastConfig);
        long originalLength = new FileInfo(vaultPath).Length;

        // Simulate an interrupted write leaving a stray .tmp file
        string strayTmp = vaultPath + ".tmp";
        File.WriteAllText(strayTmp, "Incomplete garbage write from interrupted process");

        // The primary vault should still unlock normally
        string extractDir = Path.Combine(_testRoot, "extract");
        VaultManager.UnlockVault(vaultPath, profileId, password, extractDir);

        Assert.True(File.Exists(Path.Combine(extractDir, "Default", "Preferences")));
    }

    [Fact]
    public void UnlockVault_WithCorruptedHeader_ThrowsInvalidDataException()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string workingDir = Path.Combine(_testRoot, "working");

        VaultManager.InitializeNewVault(vaultPath, profileId, "Pass123!", _fastConfig);

        // Corrupt magic header
        byte[] bytes = File.ReadAllBytes(vaultPath);
        bytes[0] = (byte)'X';
        bytes[1] = (byte)'X';
        File.WriteAllBytes(vaultPath, bytes);

        Assert.Throws<InvalidDataException>(() =>
            VaultManager.UnlockVault(vaultPath, profileId, "Pass123!", workingDir));
    }
}
