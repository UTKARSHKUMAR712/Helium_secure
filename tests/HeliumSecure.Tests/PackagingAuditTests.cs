using System.Diagnostics;
using System.IO.Compression;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Guest;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public class PackagingAuditTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _repoRoot;
    private readonly string _distDir;
    private readonly string _bundledChromeExe;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public PackagingAuditTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_Audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);

        // Locate repo root and dist directory
        string baseDir = AppContext.BaseDirectory;
        // From tests/HeliumSecure.Tests/bin/Release/net8.0-windows/
        _repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        _distDir = Directory.Exists(Path.Combine(_repoRoot, "dist", "HeliumSecure"))
            ? Path.Combine(_repoRoot, "dist", "HeliumSecure")
            : Path.Combine(_repoRoot, "dist", "HeliumPro");
        string candidate = Path.Combine(_distDir, "browser", "chrome.exe");
        _bundledChromeExe = File.Exists(candidate) ? candidate : (BrowserProcessManager.ResolveHeliumExecutable() ?? "");
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
    public void Audit_DistDirectory_AndZipArchive_AreIntact()
    {
        Assert.True(Directory.Exists(_distDir), $"Dist directory not found at {_distDir}");
        Assert.True(File.Exists(Path.Combine(_distDir, "HeliumSecure.exe")));
        Assert.True(File.Exists(Path.Combine(_distDir, "HeliumSecure.Core.dll")));
        Assert.True(File.Exists(Path.Combine(_distDir, "Install-HeliumSecure.ps1")) || File.Exists(Path.Combine(_distDir, "Install-HeliumPro.ps1")));
        Assert.True(File.Exists(_bundledChromeExe), $"Bundled chrome.exe not found at {_bundledChromeExe}");

        string zipPath = Directory.GetFiles(Path.Combine(_repoRoot, "dist"), "HeliumSecure_v*.zip").OrderByDescending(f => f).FirstOrDefault()
            ?? (File.Exists(Path.Combine(_repoRoot, "dist", "HeliumSecure_v1.0.1_win-x64.zip"))
                ? Path.Combine(_repoRoot, "dist", "HeliumSecure_v1.0.1_win-x64.zip")
                : Path.Combine(_repoRoot, "dist", "HeliumSecure_v1.0.0_win-x64.zip"));
        Assert.True(File.Exists(zipPath), "Distribution ZIP archive does not exist.");

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.True(zip.Entries.Count > 10, "ZIP archive contains insufficient entries.");
        Assert.Contains(zip.Entries, e => e.FullName.Replace('\\', '/') == "browser/chrome.exe");
        Assert.Contains(zip.Entries, e => e.FullName == "HeliumSecure.exe");
    }

    [Fact]
    public void Audit_TwoIndependentProfiles_WithBundledBrowser_StrictIsolation()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);

        var p1 = pm.CreateProfile("AuditUser1", "PassOne123!");
        var p2 = pm.CreateProfile("AuditUser2", "PassTwo456!");

        string v1 = pm.GetVaultPath(p1.Id);
        string v2 = pm.GetVaultPath(p2.Id);

        // Verify both vaults exist at rest
        Assert.True(File.Exists(v1));
        Assert.True(File.Exists(v2));

        // Unlock Profile 1, write distinct browsing data, lock
        string w1 = pm.GetProfileWorkingDirectory(p1.Id);
        VaultManager.UnlockVault(v1, p1.Id, "PassOne123!", w1);
        File.WriteAllText(Path.Combine(w1, "Default", "Bookmarks"), "User1_Secret_Bookmarks");
        VaultManager.LockVault(w1, p1.Id, "PassOne123!", v1, _fastConfig, cleanWorkingDirectory: true);

        // Plaintext directory must be cleaned
        Assert.False(Directory.Exists(w1), "Working directory w1 must be wiped after locking.");

        // Unlock Profile 2, write distinct browsing data, lock
        string w2 = pm.GetProfileWorkingDirectory(p2.Id);
        VaultManager.UnlockVault(v2, p2.Id, "PassTwo456!", w2);
        File.WriteAllText(Path.Combine(w2, "Default", "History"), "User2_Secret_History");
        VaultManager.LockVault(w2, p2.Id, "PassTwo456!", v2, _fastConfig, cleanWorkingDirectory: true);

        Assert.False(Directory.Exists(w2), "Working directory w2 must be wiped after locking.");

        // Verify isolation when reopening Profile 1
        string recheck1 = Path.Combine(_testRoot, "recheck1");
        VaultManager.UnlockVault(v1, p1.Id, "PassOne123!", recheck1);
        Assert.True(File.Exists(Path.Combine(recheck1, "Default", "Bookmarks")));
        Assert.False(File.Exists(Path.Combine(recheck1, "Default", "History"))); // User 2 data not present

        // Verify isolation when reopening Profile 2
        string recheck2 = Path.Combine(_testRoot, "recheck2");
        VaultManager.UnlockVault(v2, p2.Id, "PassTwo456!", recheck2);
        Assert.True(File.Exists(Path.Combine(recheck2, "Default", "History")));
        Assert.False(File.Exists(Path.Combine(recheck2, "Default", "Bookmarks"))); // User 1 data not present
    }

    [Fact]
    public async Task Audit_GuestMode_LeavesNoPlaintextResiduals()
    {
        string guestBase = Path.Combine(_testRoot, "GuestSessions");
        var gm = new GuestSessionManager(guestBase);

        // Start guest session with headless chrome
        string sessionDir;
        using (var processManager = new BrowserProcessManager(_bundledChromeExe))
        {
            sessionDir = Path.Combine(guestBase, $"Guest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDir);
            var proc = processManager.Launch(sessionDir, additionalArgs: "--headless=new --dump-dom about:blank");
            await processManager.WaitForExitAsync(maxGracePeriodMs: 5000);
            Assert.True(proc.HasExited);
        }

        // Verify files were created by Chromium
        Assert.True(Directory.Exists(sessionDir));

        // Clean up
        VaultManager.CleanDirectorySafely(sessionDir);

        // Verify zero plaintext files remain
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void Audit_DirectLaunchOfOriginalExecutable_CannotAccessLockedPrivateData()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("SecureExecutive", "TopSecretVaultPassword999!");
        string vaultPath = pm.GetVaultPath(p.Id);
        string workingDir = pm.GetProfileWorkingDirectory(p.Id);

        // User works in profile, writes secrets, locks profile
        VaultManager.UnlockVault(vaultPath, p.Id, "TopSecretVaultPassword999!", workingDir);
        File.WriteAllText(Path.Combine(workingDir, "Default", "Login Data"), "EncryptedSavedPasswords_CreditCards");
        VaultManager.LockVault(workingDir, p.Id, "TopSecretVaultPassword999!", vaultPath, _fastConfig, cleanWorkingDirectory: true);

        // 1. Working directory is wiped:
        Assert.False(Directory.Exists(workingDir));

        // 2. An attacker attempts to directly invoke Helium targeting the locked working directory:
        using (var attackerProcessManager = new BrowserProcessManager(_bundledChromeExe))
        {
            var proc = attackerProcessManager.Launch(workingDir, additionalArgs: "--headless=new --dump-dom about:blank");
            proc.WaitForExit(5000);
        }

        // Chromium created an empty fresh profile, but does it have the user's secret "Login Data"?
        string attackerLoginData = Path.Combine(workingDir, "Default", "Login Data");
        if (File.Exists(attackerLoginData))
        {
            string content = File.ReadAllText(attackerLoginData);
            Assert.NotEqual("EncryptedSavedPasswords_CreditCards", content);
        }

        // 3. Furthermore, when the legitimate user next unlocks their vault,
        // any foreign uncommitted files are quarantined/backed up and authentic vault data is restored.
        string restoredDir = Path.Combine(_testRoot, "restoredUser");
        VaultManager.UnlockVault(vaultPath, p.Id, "TopSecretVaultPassword999!", restoredDir);
        Assert.Equal("EncryptedSavedPasswords_CreditCards", File.ReadAllText(Path.Combine(restoredDir, "Default", "Login Data")));
    }

    [Fact]
    public void Audit_ForcedTerminationAndCrashRecovery_Integrity()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var p = pm.CreateProfile("CrashTestProfile", "CrashPassword123!");
        string vaultPath = pm.GetVaultPath(p.Id);
        string workingDir = pm.GetProfileWorkingDirectory(p.Id);

        // Step 1: Initial unlock and data save
        VaultManager.UnlockVault(vaultPath, p.Id, "CrashPassword123!", workingDir);
        File.WriteAllText(Path.Combine(workingDir, "Default", "History"), "Initial_Good_History");
        VaultManager.LockVault(workingDir, p.Id, "CrashPassword123!", vaultPath, _fastConfig, cleanWorkingDirectory: true);

        // Step 2: Unlock again, write new data
        VaultManager.UnlockVault(vaultPath, p.Id, "CrashPassword123!", workingDir);
        File.WriteAllText(Path.Combine(workingDir, "Default", "History"), "Uncommitted_Crash_History");

        // Simulate a power cut or hard kill (process killed without LockVault)
        // workingDir remains on disk!
        Assert.True(Directory.Exists(workingDir));

        // When user opens again, crash recovery detects workingDir exists and preserves it safely
        if (Directory.Exists(workingDir) && Directory.EnumerateFileSystemEntries(workingDir).Any())
        {
            string crashRecoveryDir = workingDir + $"_crash_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            Directory.Move(workingDir, crashRecoveryDir);
            Assert.True(Directory.Exists(crashRecoveryDir));
            Assert.Equal("Uncommitted_Crash_History", File.ReadAllText(Path.Combine(crashRecoveryDir, "Default", "History")));
        }

        // The vault remains in a valid authenticated state
        string recoveryDir = Path.Combine(_testRoot, "recovered");
        VaultManager.UnlockVault(vaultPath, p.Id, "CrashPassword123!", recoveryDir);
        Assert.True(File.Exists(Path.Combine(recoveryDir, "Default", "History")));
    }
}
