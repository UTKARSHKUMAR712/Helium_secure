using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;

namespace HeliumSecure.Tests;

public class ProfileManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    public ProfileManagerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_ProfileTests_" + Guid.NewGuid().ToString("N"));
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
    public void CreateProfile_AddsToManifest_AndCreatesVault()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("Personal", "MyPassword123!");

        Assert.NotNull(profile);
        Assert.Equal("Personal", profile.Name);
        Assert.True(File.Exists(pm.GetVaultPath(profile.Id)));

        var loaded = pm.ListProfiles();
        Assert.Single(loaded);
        Assert.Equal("Personal", loaded[0].Name);
    }

    [Fact]
    public void CreateDuplicateProfile_ThrowsInvalidOperationException()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        pm.CreateProfile("Work", "Password123!");

        Assert.Throws<InvalidOperationException>(() =>
            pm.CreateProfile("Work", "Password123!"));
    }

    [Fact]
    public void RenameProfile_UpdatesManifest()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("OldName", "Password123!");

        pm.RenameProfile(profile.Id, "NewName");

        var loaded = pm.ListProfiles();
        Assert.Equal("NewName", loaded[0].Name);
    }

    [Fact]
    public void DeleteProfile_WithCorrectPassword_RemovesVaultAndManifest()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("TempProfile", "CorrectPass123!");

        pm.DeleteProfile(profile.Id, "CorrectPass123!");

        Assert.Empty(pm.ListProfiles());
        Assert.False(File.Exists(pm.GetVaultPath(profile.Id)));
    }

    [Fact]
    public void DeleteProfile_WithWrongPassword_ThrowsAndKeepsProfile()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("ImportantProfile", "CorrectPass123!");

        Assert.ThrowsAny<CryptographicException>(() =>
            pm.DeleteProfile(profile.Id, "WrongPass!"));

        Assert.Single(pm.ListProfiles());
        Assert.True(File.Exists(pm.GetVaultPath(profile.Id)));
    }

    [Fact]
    public void ProfileLock_PreventsSimultaneousUnlock()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("ConcurrentProfile", "Password123!");

        string workingDir = pm.GetProfileWorkingDirectory(profile.Id);
        Directory.CreateDirectory(workingDir);
        string lockFile = Path.Combine(pm.GetProfileWorkingDirectory(profile.Id) + ".lock");

        // Simulate an active lock file
        using var stream = new FileStream(lockFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.True(pm.IsProfileLocked(profile.Id));
    }

    [Fact]
    public void ChangePassword_WithCorrectCurrentPassword_ReEncryptsAndAllowsNewPassword()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("PasswordChangeTest", "CurrentPass123!");

        pm.ChangePassword(profile.Id, "CurrentPass123!", "NewSecretPass456!");

        // Old password should now fail validation
        var failResult = pm.ValidatePasswordAttempt(profile.Id, "CurrentPass123!");
        Assert.Equal(AuthenticationStatus.InvalidPassword, failResult.Status);

        // New password should succeed
        var successResult = pm.ValidatePasswordAttempt(profile.Id, "NewSecretPass456!");
        Assert.Equal(AuthenticationStatus.Success, successResult.Status);
    }

    [Fact]
    public void ChangePassword_WithWrongCurrentPassword_FailsAndEnforcesAttemptPolicy()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("PasswordPolicyTest", "OriginalPass123!");

        // First wrong attempt
        Assert.Throws<AuthenticationFailedException>(() =>
            pm.ChangePassword(profile.Id, "WrongPass1!", "NewPass123!"));
        Assert.Equal(2, pm.GetRemainingAttempts(profile.Id, out _));

        // Second wrong attempt
        Assert.Throws<AuthenticationFailedException>(() =>
            pm.ChangePassword(profile.Id, "WrongPass2!", "NewPass123!"));
        Assert.Equal(1, pm.GetRemainingAttempts(profile.Id, out _));

        // Third wrong attempt -> locks out
        Assert.Throws<ProfileLockedOutException>(() =>
            pm.ChangePassword(profile.Id, "WrongPass3!", "NewPass123!"));
        Assert.True(pm.IsProfileLockedOut(profile.Id, out _, out _));
    }

    [Fact]
    public void ChangePassword_WhenProfileActive_ThrowsProfileAlreadyLockedException()
    {
        var pm = new ProfileManager(_testRoot, _fastConfig);
        var profile = pm.CreateProfile("ActiveProfileTest", "CurrentPass123!");

        string workingDir = pm.GetProfileWorkingDirectory(profile.Id);
        Directory.CreateDirectory(workingDir);
        string lockFile = Path.Combine(pm.GetProfileWorkingDirectory(profile.Id) + ".lock");

        using (var stream = new FileStream(lockFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<ProfileAlreadyLockedException>(() =>
                pm.ChangePassword(profile.Id, "CurrentPass123!", "NewPass456!"));
        }
    }
}
