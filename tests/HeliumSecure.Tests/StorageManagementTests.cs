using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Storage;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public class StorageManagementTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public StorageManagementTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_StorageTests_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void StorageFormatHelper_FormatsBytesCorrectly(long bytes, string expected)
    {
        string actual = StorageFormatHelper.FormatBytes(bytes);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CalculateDirectorySize_AccuratelyAccountsNestedDirectories_WithoutDoubleCounting()
    {
        string subDir1 = Path.Combine(_testRoot, "Level1");
        string subDir2 = Path.Combine(subDir1, "Level2");
        Directory.CreateDirectory(subDir2);

        byte[] data1 = new byte[1000];
        byte[] data2 = new byte[2500];
        byte[] data3 = new byte[4000];

        File.WriteAllBytes(Path.Combine(_testRoot, "file1.bin"), data1);
        File.WriteAllBytes(Path.Combine(subDir1, "file2.bin"), data2);
        File.WriteAllBytes(Path.Combine(subDir2, "file3.bin"), data3);

        long calculated = StorageService.CalculateDirectorySize(_testRoot, out bool isComplete);

        Assert.True(isComplete);
        Assert.Equal(7500, calculated);
    }

    [Fact]
    public void CalculateDirectorySize_HandlesMissingDirectoryGracefully()
    {
        string nonExistent = Path.Combine(_testRoot, "NonExistentFolder_123");
        long size = StorageService.CalculateDirectorySize(nonExistent, out bool isComplete);

        Assert.True(isComplete);
        Assert.Equal(0, size);
    }

    [Fact]
    public void StorageOverview_AccuratelyDistinguishesAppAndUserData_WithoutDoubleCounting()
    {
        string mockAppDir = Path.Combine(_testRoot, "App");
        string mockBrowserDir = Path.Combine(mockAppDir, "browser");
        Directory.CreateDirectory(mockBrowserDir);

        File.WriteAllBytes(Path.Combine(mockAppDir, "HeliumSecure.exe"), new byte[10000]);
        File.WriteAllBytes(Path.Combine(mockBrowserDir, "chrome.exe"), new byte[50000]);

        string mockDataDir = Path.Combine(_testRoot, "UserData");
        var pm = new ProfileManager(mockDataDir, _fastConfig);
        var p1 = pm.CreateProfile("User1", "Pass123!");

        var overview = StorageService.GetStorageOverview(mockDataDir, mockAppDir);

        Assert.True(overview.IsMeasurementComplete);
        Assert.Equal(60000, overview.InstalledAppSizeBytes);
        Assert.Equal(50000, overview.BundledBrowserSizeBytes);
        Assert.Equal(10000, overview.LauncherSizeBytes);
        Assert.True(overview.TotalProfilesSizeBytes > 0);
        Assert.Equal(overview.InstalledAppSizeBytes + overview.TotalProfilesSizeBytes, overview.TotalFootprintBytes);
    }

    [Fact]
    public void ProfileStorageDetails_AccuratelyIdentifiesBackupsAndCrashData()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("StorageAuditUser", "StrongPass123!");
        string vaultPath = pm.GetVaultPath(p.Id);
        string bakPath = vaultPath + ".bak";

        // Create a backup snapshot
        File.WriteAllBytes(bakPath, new byte[12345]);

        // Create a simulated crash recovery directory
        string profilesDir = Path.Combine(_testRoot, "Profiles");
        string crashDir = Path.Combine(profilesDir, $"{p.Id:D}_crash_123456789");
        Directory.CreateDirectory(crashDir);
        File.WriteAllBytes(Path.Combine(crashDir, "uncommitted_history.bin"), new byte[5432]);

        var details = StorageService.GetProfileStorageDetails(pm, p.Id, _testRoot);

        Assert.True(details.IsMeasurementComplete);
        Assert.Equal("StorageAuditUser", details.Name);
        Assert.True(details.VaultSizeBytes > 0);
        Assert.Equal(12345, details.BackupSizeBytes);
        Assert.Equal(5432, details.CrashRecoverySizeBytes);
        Assert.Equal(details.VaultSizeBytes + details.BackupSizeBytes + details.WorkingDirectorySizeBytes + details.CrashRecoverySizeBytes, details.TotalSizeBytes);
    }

    [Fact]
    public void ClearProfileStorage_PurgesSelectedCategories_PreservesUnselectedDataAndBackups()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("ClearTestUser", "Pass123!");
        string vaultPath = pm.GetVaultPath(p.Id);
        string workDir = pm.GetProfileWorkingDirectory(p.Id);

        // 1. Populate profile with Cache, History, Cookies, and Bookmarks
        VaultManager.UnlockVault(vaultPath, p.Id, "Pass123!", workDir);

        string cacheDir = Path.Combine(workDir, "Default", "Cache");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(Path.Combine(cacheDir, "data_0"), "CacheDataContent12345");

        string cookiesDir = Path.Combine(workDir, "Default", "Network");
        Directory.CreateDirectory(cookiesDir);
        File.WriteAllText(Path.Combine(cookiesDir, "Cookies"), "SecretUserAuthCookies");

        File.WriteAllText(Path.Combine(workDir, "Default", "History"), "VisitedSitesHistory");
        File.WriteAllText(Path.Combine(workDir, "Default", "Bookmarks"), "ImportantUserBookmarks");

        VaultManager.LockVault(workDir, p.Id, "Pass123!", vaultPath, _fastConfig, cleanWorkingDirectory: true);

        // Create backup snapshot before clearing
        string bakPath = vaultPath + ".bak";
        File.Copy(vaultPath, bakPath, overwrite: true);
        long originalBakLength = new FileInfo(bakPath).Length;

        // 2. Clear ONLY Cache
        var options = new ClearStorageOptions(
            ClearCache: true,
            ClearHistory: false,
            ClearCookiesAndSiteData: false,
            ClearDownloads: false
        );

        StorageService.ClearProfileStorage(pm, p.Id, "Pass123!", options, _fastConfig);

        // 3. Verify backup is preserved
        Assert.True(File.Exists(bakPath));
        Assert.Equal(originalBakLength, new FileInfo(bakPath).Length);

        // 4. Unlock and verify: Cache is gone, Bookmarks, Cookies, and History are intact
        string verifyDir = Path.Combine(_testRoot, "verifyPrune");
        VaultManager.UnlockVault(vaultPath, p.Id, "Pass123!", verifyDir);

        Assert.False(Directory.Exists(Path.Combine(verifyDir, "Default", "Cache")));
        Assert.True(File.Exists(Path.Combine(verifyDir, "Default", "Bookmarks")));
        Assert.Equal("ImportantUserBookmarks", File.ReadAllText(Path.Combine(verifyDir, "Default", "Bookmarks")));
        Assert.True(File.Exists(Path.Combine(verifyDir, "Default", "History")));
        Assert.True(File.Exists(Path.Combine(verifyDir, "Default", "Network", "Cookies")));
    }

    [Fact]
    public void ClearProfileStorage_PurgesCookiesAndHistory_WhenRequested()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("ClearCookiesUser", "Pass123!");
        string vaultPath = pm.GetVaultPath(p.Id);
        string workDir = pm.GetProfileWorkingDirectory(p.Id);

        // Populate
        VaultManager.UnlockVault(vaultPath, p.Id, "Pass123!", workDir);
        string cookiesDir = Path.Combine(workDir, "Default", "Network");
        Directory.CreateDirectory(cookiesDir);
        File.WriteAllText(Path.Combine(cookiesDir, "Cookies"), "AuthSessionToken");
        File.WriteAllText(Path.Combine(workDir, "Default", "History"), "HistoryLog");
        File.WriteAllText(Path.Combine(workDir, "Default", "Bookmarks"), "BookmarksKeep");
        VaultManager.LockVault(workDir, p.Id, "Pass123!", vaultPath, _fastConfig, cleanWorkingDirectory: true);

        // Clear History & Cookies
        var options = new ClearStorageOptions(
            ClearCache: false,
            ClearHistory: true,
            ClearCookiesAndSiteData: true,
            ClearDownloads: false
        );

        StorageService.ClearProfileStorage(pm, p.Id, "Pass123!", options, _fastConfig);

        string verifyDir = Path.Combine(_testRoot, "verifyCookiesPrune");
        VaultManager.UnlockVault(vaultPath, p.Id, "Pass123!", verifyDir);

        Assert.False(File.Exists(Path.Combine(verifyDir, "Default", "Network", "Cookies")));
        Assert.False(File.Exists(Path.Combine(verifyDir, "Default", "History")));
        Assert.True(File.Exists(Path.Combine(verifyDir, "Default", "Bookmarks")));
    }

    [Fact]
    public void ClearProfileStorage_ThrowsProfileAlreadyLockedException_WhenBrowserActive()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("ActiveUser", "Pass123!");

        // Simulate active session lock
        string lockFile = pm.GetProfileWorkingDirectory(p.Id) + ".lock";
        using var lockStream = new FileStream(lockFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.True(pm.IsProfileLocked(p.Id));

        var options = new ClearStorageOptions(ClearCache: true);

        Assert.Throws<ProfileAlreadyLockedException>(() =>
            StorageService.ClearProfileStorage(pm, p.Id, "Pass123!", options, _fastConfig));
    }

    [Fact]
    public void ClearProfileStorage_WrongPassword_LeavesVaultUnchanged()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("WrongPassUser", "CorrectPassword123!");
        string vaultPath = pm.GetVaultPath(p.Id);
        byte[] originalVaultBytes = File.ReadAllBytes(vaultPath);

        var options = new ClearStorageOptions(ClearCache: true);

        Assert.ThrowsAny<CryptographicException>(() =>
            StorageService.ClearProfileStorage(pm, p.Id, "IncorrectPass!", options, _fastConfig));

        // Vault is unaltered
        Assert.Equal(originalVaultBytes, File.ReadAllBytes(vaultPath));
    }

    [Fact]
    public void DeleteProfileCompletely_RemovesVaultBackupsWorkingAndCrashDirectories_WithIsolation()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var targetProfile = pm.CreateProfile("TargetDelete", "Pass123!");
        var otherProfile = pm.CreateProfile("OtherKept", "Pass456!");

        string targetVault = pm.GetVaultPath(targetProfile.Id);
        string targetBak = targetVault + ".bak";
        File.WriteAllText(targetBak, "BackupData");

        string profilesDir = Path.Combine(_testRoot, "Profiles");
        string targetCrashDir = Path.Combine(profilesDir, $"{targetProfile.Id:D}_crash_9999");
        Directory.CreateDirectory(targetCrashDir);
        File.WriteAllText(Path.Combine(targetCrashDir, "crash.log"), "CrashLog");

        // Delete target
        StorageService.DeleteProfileCompletely(pm, targetProfile.Id, "Pass123!", _testRoot);

        // Target profile elements removed
        Assert.False(File.Exists(targetVault));
        Assert.False(File.Exists(targetBak));
        Assert.False(Directory.Exists(targetCrashDir));
        Assert.DoesNotContain(pm.ListProfiles(), p => p.Id == targetProfile.Id);

        // Other profile is 100% untouched
        Assert.True(File.Exists(pm.GetVaultPath(otherProfile.Id)));
        Assert.Contains(pm.ListProfiles(), p => p.Id == otherProfile.Id);
    }
}
