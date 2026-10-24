using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public class CrossProfileIsolationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public CrossProfileIsolationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_IsolationTests_" + Guid.NewGuid().ToString("N"));
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
    public void TwoProfiles_MaintainStrictDataIsolation()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);

        // Create Profile A
        var profileA = pm.CreateProfile("ProfileA", "PassA_12345!");
        string workDirA = pm.GetProfileWorkingDirectory(profileA.Id);

        // Create Profile B
        var profileB = pm.CreateProfile("ProfileB", "PassB_98765!");
        string workDirB = pm.GetProfileWorkingDirectory(profileB.Id);

        // Unlock Profile A and write distinct history and bookmark files
        VaultManager.UnlockVault(pm.GetVaultPath(profileA.Id), profileA.Id, "PassA_12345!", workDirA);
        File.WriteAllText(Path.Combine(workDirA, "Default", "History"), "ProfileA_BrowserHistory_Data");
        File.WriteAllText(Path.Combine(workDirA, "Default", "Bookmarks"), "ProfileA_Bookmarks_Data");
        VaultManager.LockVault(workDirA, profileA.Id, "PassA_12345!", pm.GetVaultPath(profileA.Id), _fastConfig);

        // Unlock Profile B and write different data
        VaultManager.UnlockVault(pm.GetVaultPath(profileB.Id), profileB.Id, "PassB_98765!", workDirB);
        File.WriteAllText(Path.Combine(workDirB, "Default", "History"), "ProfileB_Different_History");
        File.WriteAllText(Path.Combine(workDirB, "Default", "Cookies"), "ProfileB_Secret_Cookies");
        VaultManager.LockVault(workDirB, profileB.Id, "PassB_98765!", pm.GetVaultPath(profileB.Id), _fastConfig);

        // Reopen Profile A in a fresh directory and verify B's data is completely absent
        string freshDirA = Path.Combine(_testRoot, "verifyA");
        VaultManager.UnlockVault(pm.GetVaultPath(profileA.Id), profileA.Id, "PassA_12345!", freshDirA);

        Assert.True(File.Exists(Path.Combine(freshDirA, "Default", "History")));
        Assert.Equal("ProfileA_BrowserHistory_Data", File.ReadAllText(Path.Combine(freshDirA, "Default", "History")));
        Assert.True(File.Exists(Path.Combine(freshDirA, "Default", "Bookmarks")));
        Assert.False(File.Exists(Path.Combine(freshDirA, "Default", "Cookies"))); // Profile B cookies must not exist in Profile A!

        // Reopen Profile B in a fresh directory and verify A's data is absent
        string freshDirB = Path.Combine(_testRoot, "verifyB");
        VaultManager.UnlockVault(pm.GetVaultPath(profileB.Id), profileB.Id, "PassB_98765!", freshDirB);

        Assert.True(File.Exists(Path.Combine(freshDirB, "Default", "Cookies")));
        Assert.Equal("ProfileB_Secret_Cookies", File.ReadAllText(Path.Combine(freshDirB, "Default", "Cookies")));
        Assert.False(File.Exists(Path.Combine(freshDirB, "Default", "Bookmarks"))); // Profile A bookmarks must not exist in Profile B!
    }

    [Fact]
    public void ProfileVault_CannotBeDecrypted_WithAnotherProfilesKey()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profileA = pm.CreateProfile("ProfileA", "SamePassword123!");
        var profileB = pm.CreateProfile("ProfileB", "SamePassword123!");

        string testExtractDir = Path.Combine(_testRoot, "crossExtract");

        // Even though both profiles were created with identical passwords,
        // their random salts and ProfileId-bound AAD prevent cross-decrypting
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            VaultManager.UnlockVault(pm.GetVaultPath(profileA.Id), profileB.Id, "SamePassword123!", testExtractDir));
    }
}
