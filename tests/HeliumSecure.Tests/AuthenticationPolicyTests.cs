using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Storage;
using HeliumSecure.Core.Time;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Tests;

public sealed class AuthenticationPolicyTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig;
    private readonly TestClock _clock;

    public AuthenticationPolicyTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"HeliumAuthTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        // Fast argon2 config for test execution speed
        _fastConfig = new VaultCryptoConfig(
            SaltSizeBytes: 32,
            Iterations: 1,
            MemorySizeKb: 1024,
            DegreeOfParallelism: 1,
            KeySizeBytes: 32,
            NonceSizeBytes: 12,
            TagSizeBytes: 16);

        _clock = new TestClock(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
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

    private ProfileManager CreateManager() =>
        new ProfileManager(_testRoot, _fastConfig, _clock);

    [Fact]
    public void Stage1_AllowsThreeFailedAttempts_ThenLocksOutFor30Minutes()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("PolicyUser", "CorrectPassword123!");

        // Attempt 1 fails
        var res1 = pm.ValidatePasswordAttempt(profile.Id, "Wrong1");
        Assert.Equal(AuthenticationStatus.InvalidPassword, res1.Status);
        Assert.Equal(2, res1.RemainingAttempts);
        Assert.False(res1.InSecondStage);

        // Attempt 2 fails
        var res2 = pm.ValidatePasswordAttempt(profile.Id, "Wrong2");
        Assert.Equal(AuthenticationStatus.InvalidPassword, res2.Status);
        Assert.Equal(1, res2.RemainingAttempts);
        Assert.False(res2.InSecondStage);

        // Attempt 3 fails -> Stage 1 lockout triggered
        var res3 = pm.ValidatePasswordAttempt(profile.Id, "Wrong3");
        Assert.Equal(AuthenticationStatus.LockedOut, res3.Status);
        Assert.Equal(0, res3.RemainingAttempts);
        Assert.False(res3.InSecondStage);
        Assert.NotNull(res3.RemainingLockout);
        Assert.True(res3.RemainingLockout.Value.TotalMinutes >= 29);

        // Confirm profile is flagged as locked out
        Assert.True(pm.IsProfileLockedOut(profile.Id, out var remaining, out var inSecondStage));
        Assert.False(inSecondStage);
        Assert.True(remaining.TotalMinutes >= 29);
    }

    [Fact]
    public void ActiveLockout_RejectsCorrectPassword_WithoutVerifying()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("LockoutUser", "SecretPassword456!");

        // Cause 3 failures to trigger 30-minute lockout
        pm.ValidatePasswordAttempt(profile.Id, "Bad1");
        pm.ValidatePasswordAttempt(profile.Id, "Bad2");
        pm.ValidatePasswordAttempt(profile.Id, "Bad3");

        // Now attempt with the CORRECT password while locked out
        var lockoutResult = pm.ValidatePasswordAttempt(profile.Id, "SecretPassword456!");
        Assert.Equal(AuthenticationStatus.LockedOut, lockoutResult.Status);
        Assert.False(lockoutResult.Success);

        // Authenticate method should throw ProfileLockedOutException
        var ex = Assert.Throws<ProfileLockedOutException>(() =>
            pm.Authenticate(profile.Id, "SecretPassword456!"));
        Assert.False(ex.InSecondStage);
        Assert.True(ex.RemainingLockout.TotalMinutes >= 29);
    }

    [Fact]
    public void Lockout_PersistsAcrossApplicationRestart()
    {
        var pm1 = CreateManager();
        var profile = pm1.CreateProfile("RestartUser", "SafePass123!");

        // Trigger lockout
        pm1.ValidatePasswordAttempt(profile.Id, "W1");
        pm1.ValidatePasswordAttempt(profile.Id, "W2");
        pm1.ValidatePasswordAttempt(profile.Id, "W3");

        // Advance 10 minutes in time
        _clock.Advance(TimeSpan.FromMinutes(10));

        // Create a new ProfileManager pointing to the same storage directory (simulating app restart)
        var pm2 = new ProfileManager(_testRoot, _fastConfig, _clock);

        Assert.True(pm2.IsProfileLockedOut(profile.Id, out var remaining, out var inSecondStage));
        Assert.False(inSecondStage);
        // Lockout was 30 minutes, 10 elapsed, ~20 minutes remaining
        Assert.InRange(remaining.TotalMinutes, 19, 21);

        // Authenticate on new instance still throws ProfileLockedOutException
        var ex = Assert.Throws<ProfileLockedOutException>(() =>
            pm2.Authenticate(profile.Id, "SafePass123!"));
        Assert.InRange(ex.RemainingLockout.TotalMinutes, 19, 21);
    }

    [Fact]
    public void Lockout_ClockRollback_DoesNotBypassLockout()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("ClockTamperUser", "Password123!");

        // Trigger lockout at T = 12:00
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Roll clock backward by 15 minutes (to 11:45)
        _clock.Set(new DateTimeOffset(2026, 10, 24, 11, 45, 0, TimeSpan.Zero));

        // Lockout must not be bypassed!
        Assert.True(pm.IsProfileLockedOut(profile.Id, out var remaining, out _));
        Assert.True(remaining.TotalMinutes >= 30, "Clock rollback must extend/preserve lockout, never expire early.");

        var res = pm.ValidatePasswordAttempt(profile.Id, "Password123!");
        Assert.Equal(AuthenticationStatus.LockedOut, res.Status);
    }

    [Fact]
    public void Stage2_TransitionsAfter30MinutesExpire()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("Stage2User", "PassStage2!");

        // Trigger lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Advance clock by exactly 30 minutes
        _clock.Advance(TimeSpan.FromMinutes(30));

        // Lockout has elapsed, profile enters Stage 2
        Assert.False(pm.IsProfileLockedOut(profile.Id, out _, out var inSecondStage));
        Assert.True(inSecondStage);
        Assert.Equal(5, pm.GetRemainingAttempts(profile.Id, out var stage2Check));
        Assert.True(stage2Check);
    }

    [Fact]
    public void Stage2_Success_ResetsAllAttemptCountersToNormal()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("RecoveryUser", "CorrectKey999!");

        // Trigger stage 1 lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Advance 30 minutes
        _clock.Advance(TimeSpan.FromMinutes(30));

        // In Stage 2, fail twice
        var s2_1 = pm.ValidatePasswordAttempt(profile.Id, "WrongS2_1");
        Assert.Equal(AuthenticationStatus.InvalidPassword, s2_1.Status);
        Assert.Equal(4, s2_1.RemainingAttempts);
        Assert.True(s2_1.InSecondStage);

        var s2_2 = pm.ValidatePasswordAttempt(profile.Id, "WrongS2_2");
        Assert.Equal(AuthenticationStatus.InvalidPassword, s2_2.Status);
        Assert.Equal(3, s2_2.RemainingAttempts);
        Assert.True(s2_2.InSecondStage);

        // Now enter the CORRECT password!
        var successRes = pm.ValidatePasswordAttempt(profile.Id, "CorrectKey999!");
        Assert.Equal(AuthenticationStatus.Success, successRes.Status);
        Assert.True(successRes.Success);
        Assert.False(successRes.InSecondStage);
        Assert.Equal(3, successRes.RemainingAttempts);

        // Verify profile state is fully reset
        int remaining = pm.GetRemainingAttempts(profile.Id, out bool inStage2);
        Assert.Equal(3, remaining);
        Assert.False(inStage2);
        Assert.False(pm.IsProfileLockedOut(profile.Id, out _, out _));
    }

    [Fact]
    public void Stage2_AllowsExactlyFiveFailures_ThenTerminallyDestroysProfile()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("DestructionUser", "TopSecret888!");

        string vaultPath = pm.GetVaultPath(profile.Id);
        string bakPath = vaultPath + ".bak";
        // Create a dummy backup to test that backups are also destroyed
        File.Copy(vaultPath, bakPath, overwrite: true);

        // Create crash recovery directory to verify crash dirs are also purged
        string crashDir = Path.Combine(pm.GetProfileWorkingDirectory(profile.Id) + "_crash_12345");
        Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "recovered.txt"), "sensitive crash data");

        // Stage 1: 3 failed attempts
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Advance 30 minutes
        _clock.Advance(TimeSpan.FromMinutes(30));

        // Stage 2: Attempt 1 (remaining 4)
        var s2_1 = pm.ValidatePasswordAttempt(profile.Id, "Bad_1");
        Assert.Equal(4, s2_1.RemainingAttempts);
        Assert.True(File.Exists(vaultPath));

        // Stage 2: Attempt 2 (remaining 3)
        var s2_2 = pm.ValidatePasswordAttempt(profile.Id, "Bad_2");
        Assert.Equal(3, s2_2.RemainingAttempts);
        Assert.True(File.Exists(vaultPath));

        // Stage 2: Attempt 3 (remaining 2)
        var s2_3 = pm.ValidatePasswordAttempt(profile.Id, "Bad_3");
        Assert.Equal(2, s2_3.RemainingAttempts);
        Assert.True(File.Exists(vaultPath));

        // Stage 2: Attempt 4 (remaining 1)
        var s2_4 = pm.ValidatePasswordAttempt(profile.Id, "Bad_4");
        Assert.Equal(1, s2_4.RemainingAttempts);
        Assert.True(File.Exists(vaultPath));
        Assert.True(File.Exists(bakPath));
        Assert.True(Directory.Exists(crashDir));

        // Stage 2: Attempt 5 (Terminal Destruction!)
        var s2_5 = pm.ValidatePasswordAttempt(profile.Id, "Bad_5");
        Assert.Equal(AuthenticationStatus.TerminalDestruction, s2_5.Status);
        Assert.Equal(0, s2_5.RemainingAttempts);

        // Assert all artifacts for this profile are permanently destroyed
        Assert.False(File.Exists(vaultPath), "Vault file must be permanently deleted.");
        Assert.False(File.Exists(bakPath), "Backup vault file must be permanently deleted.");
        Assert.False(File.Exists(vaultPath + ".tmp"), "Temporary vault file must not exist.");
        Assert.False(Directory.Exists(pm.GetProfileWorkingDirectory(profile.Id)), "Working directory must not exist.");
        Assert.False(Directory.Exists(crashDir), "Crash recovery directory must be purged.");

        // Profile must be purged from manifest
        Assert.DoesNotContain(pm.ListProfiles(), p => p.Id == profile.Id);

        // Subsequent authenticate attempt throws KeyNotFoundException or TerminalDestructionException
        Assert.ThrowsAny<Exception>(() => pm.Authenticate(profile.Id, "TopSecret888!"));
    }

    [Fact]
    public void TerminalDestruction_PreservesOtherProfilesAndGlobalManifest()
    {
        var pm = CreateManager();
        var victimProfile = pm.CreateProfile("VictimProfile", "VictimPass123!");
        var survivorProfile = pm.CreateProfile("SurvivorProfile", "SurvivorPass123!");

        string victimVault = pm.GetVaultPath(victimProfile.Id);
        string survivorVault = pm.GetVaultPath(survivorProfile.Id);

        // Stage 1 failures for victim
        pm.ValidatePasswordAttempt(victimProfile.Id, "V_Fail1");
        pm.ValidatePasswordAttempt(victimProfile.Id, "V_Fail2");
        pm.ValidatePasswordAttempt(victimProfile.Id, "V_Fail3");

        // Advance 30 minutes
        _clock.Advance(TimeSpan.FromMinutes(30));

        // Stage 2: 5 failures for victim
        for (int i = 0; i < 5; i++)
        {
            pm.ValidatePasswordAttempt(victimProfile.Id, $"V_Fail_{i}");
        }

        // Victim is destroyed
        Assert.False(File.Exists(victimVault));
        Assert.DoesNotContain(pm.ListProfiles(), p => p.Id == victimProfile.Id);

        // Survivor is completely unaffected!
        Assert.True(File.Exists(survivorVault));
        Assert.Contains(pm.ListProfiles(), p => p.Id == survivorProfile.Id);

        // Survivor can authenticate and unlock normally
        var survivorAuth = pm.ValidatePasswordAttempt(survivorProfile.Id, "SurvivorPass123!");
        Assert.Equal(AuthenticationStatus.Success, survivorAuth.Status);
    }

    [Fact]
    public async Task ConcurrentAttempts_AreThreadSafeAndConsistent()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("ConcurrentProfile", "SafePass123!");

        // Run 10 concurrent threads attempting incorrect passwords
        int threadCount = 10;
        var tasks = new Task<AuthenticationResult>[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(() => pm.ValidatePasswordAttempt(profile.Id, $"Wrong_{idx}"));
        }

        await Task.WhenAll(tasks);

        // All 10 tasks should complete without unhandled race condition exceptions
        // The profile must now be in locked out state (failed attempts >= 3)
        Assert.True(pm.IsProfileLockedOut(profile.Id, out _, out _));
    }

    [Fact]
    public void DeleteProfile_WhenLockedOut_ThrowsProfileLockedOutException()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("DeleteLockedProfile", "Pass123!");

        // Trigger lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Attempting to delete during lockout should throw ProfileLockedOutException
        Assert.Throws<ProfileLockedOutException>(() =>
            pm.DeleteProfile(profile.Id, "Pass123!"));
    }

    [Fact]
    public void ClearProfileStorage_WhenLockedOut_ThrowsProfileLockedOutException()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("ClearLockedProfile", "Pass123!");

        // Trigger lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Attempting to clear storage during lockout should throw ProfileLockedOutException
        Assert.Throws<ProfileLockedOutException>(() =>
            StorageService.ClearProfileStorage(pm, profile.Id, "Pass123!", new ClearStorageOptions()));
    }

    [Fact]
    public async Task UnlockAndLaunchAsync_WhenLockedOut_ThrowsProfileLockedOutException()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("UnlockLockedProfile", "Pass123!");

        // Trigger lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        await Assert.ThrowsAsync<ProfileLockedOutException>(async () =>
            await pm.UnlockAndLaunchAsync(profile.Id, "Pass123!", customHeliumExe: "cmd.exe"));
    }

    [Fact]
    public async Task UnlockAndLaunchAsync_Stage2FifthFailure_DestroysProfileAndThrowsTerminalDestructionException()
    {
        var pm = CreateManager();
        var profile = pm.CreateProfile("UnlockTerminalProfile", "Pass123!");
        string vaultPath = pm.GetVaultPath(profile.Id);

        // Stage 1 lockout
        pm.ValidatePasswordAttempt(profile.Id, "W1");
        pm.ValidatePasswordAttempt(profile.Id, "W2");
        pm.ValidatePasswordAttempt(profile.Id, "W3");

        // Advance 30 minutes
        _clock.Advance(TimeSpan.FromMinutes(30));

        // 4 failures in stage 2
        for (int i = 0; i < 4; i++)
        {
            pm.ValidatePasswordAttempt(profile.Id, $"S2_{i}");
        }

        // 5th failure via UnlockAndLaunchAsync
        await Assert.ThrowsAsync<TerminalDestructionException>(async () =>
            await pm.UnlockAndLaunchAsync(profile.Id, "WrongPass5", customHeliumExe: "cmd.exe"));

        Assert.False(File.Exists(vaultPath));
        Assert.DoesNotContain(pm.ListProfiles(), p => p.Id == profile.Id);
    }
}
