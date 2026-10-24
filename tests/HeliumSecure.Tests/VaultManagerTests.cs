using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public class VaultManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public VaultManagerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_VaultTests_" + Guid.NewGuid().ToString("N"));
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
    public void InitializeVault_And_Unlock_WithCorrectPassword_Succeeds()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string workingDir = Path.Combine(_testRoot, "working");

        VaultManager.InitializeNewVault(vaultPath, profileId, "CorrectPassw0rd!", _fastConfig);
        Assert.True(File.Exists(vaultPath));

        VaultManager.UnlockVault(vaultPath, profileId, "CorrectPassw0rd!", workingDir);

        Assert.True(File.Exists(Path.Combine(workingDir, "Default", "Preferences")));
    }

    [Fact]
    public void UnlockVault_WithWrongPassword_ThrowsCryptographicException()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string workingDir = Path.Combine(_testRoot, "working");

        VaultManager.InitializeNewVault(vaultPath, profileId, "CorrectPassw0rd!", _fastConfig);

        Assert.ThrowsAny<CryptographicException>(() =>
            VaultManager.UnlockVault(vaultPath, profileId, "WrongPassw0rd!", workingDir));
    }

    [Fact]
    public void LockVault_CommitsUpdatedFiles_AndCleanDirectory_Succeeds()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string workingDir = Path.Combine(_testRoot, "working");
        string password = "StrongMasterPassword987!";

        VaultManager.InitializeNewVault(vaultPath, profileId, password, _fastConfig);
        VaultManager.UnlockVault(vaultPath, profileId, password, workingDir);

        // Modify preferences and add a new cookie file
        string customFile = Path.Combine(workingDir, "Default", "Cookies");
        File.WriteAllText(customFile, "EncryptedCookieData123");

        // Lock vault back
        VaultManager.LockVault(workingDir, profileId, password, vaultPath, _fastConfig, cleanWorkingDirectory: true);

        // Verify working dir is wiped
        Assert.False(Directory.Exists(workingDir));

        // Unlock again and verify changes persisted in vault
        string restoredDir = Path.Combine(_testRoot, "restored");
        VaultManager.UnlockVault(vaultPath, profileId, password, restoredDir);

        Assert.True(File.Exists(Path.Combine(restoredDir, "Default", "Cookies")));
        Assert.Equal("EncryptedCookieData123", File.ReadAllText(Path.Combine(restoredDir, "Default", "Cookies")));
    }

    [Fact]
    public void TamperedVault_FallsBackToBackup_WhenBakExists()
    {
        var profileId = Guid.NewGuid();
        string vaultPath = Path.Combine(_testRoot, $"{profileId}.hsv");
        string bakPath = vaultPath + ".bak";
        string workingDir = Path.Combine(_testRoot, "working");
        string password = "TestPassword456!";

        VaultManager.InitializeNewVault(vaultPath, profileId, password, _fastConfig);

        // Create backup
        File.Copy(vaultPath, bakPath);

        // Corrupt primary vault ciphertext
        byte[] bytes = File.ReadAllBytes(vaultPath);
        bytes[^1] ^= 0x55;
        File.WriteAllBytes(vaultPath, bytes);

        // Should successfully recover from .bak
        VaultManager.UnlockVault(vaultPath, profileId, password, workingDir);
        Assert.True(File.Exists(Path.Combine(workingDir, "Default", "Preferences")));
    }
}
