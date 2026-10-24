using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Security;
using HeliumSecure.Core.Storage;
using HeliumSecure.Core.Time;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Core.Profiles;

public sealed class ProfileSession : IAsyncDisposable
{
    private readonly FileStream _lockStream;
    private readonly string _lockFilePath;
    private bool _disposed;

    public Guid ProfileId { get; }
    public string WorkingDirectory { get; }
    public BrowserProcessManager ProcessManager { get; }

    internal ProfileSession(
        Guid profileId,
        string workingDirectory,
        BrowserProcessManager processManager,
        FileStream lockStream,
        string lockFilePath)
    {
        ProfileId = profileId;
        WorkingDirectory = workingDirectory;
        ProcessManager = processManager;
        _lockStream = lockStream;
        _lockFilePath = lockFilePath;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            ProcessManager.Dispose();
            _lockStream.Dispose();
            try { File.Delete(_lockFilePath); } catch { }
            _disposed = true;
        }
        await ValueTask.CompletedTask;
    }
}

public sealed class ProfileManager
{
    private readonly string _baseDirectory;
    private readonly string _vaultsDirectory;
    private readonly string _workingDirectory;
    private readonly string _manifestPath;
    private readonly VaultCryptoConfig _cryptoConfig;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, FileStream> _activeLocks = new();
    private readonly ConcurrentDictionary<Guid, object> _profileAuthLocks = new();
    private readonly object _manifestLock = new();

    public IClock Clock => _clock;
    public ProfileManifest Manifest { get; private set; }

    public ProfileManager(string? customBaseDirectory = null, VaultCryptoConfig? cryptoConfig = null, IClock? clock = null)
    {
        _cryptoConfig = cryptoConfig ?? VaultCryptoConfig.Default;
        _clock = clock ?? SystemClock.Instance;

        string root = customBaseDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliumSecure");

        _baseDirectory = root;
        _vaultsDirectory = Path.Combine(_baseDirectory, "Vaults");
        _workingDirectory = Path.Combine(_baseDirectory, "Profiles");
        _manifestPath = Path.Combine(_baseDirectory, "manifest.json");

        NtfsSecurityHelper.SecureDirectory(_baseDirectory);
        NtfsSecurityHelper.SecureDirectory(_vaultsDirectory);
        NtfsSecurityHelper.SecureDirectory(_workingDirectory);

        Manifest = ProfileManifest.Load(_manifestPath);
    }

    private object GetAuthLock(Guid profileId) =>
        _profileAuthLocks.GetOrAdd(profileId, _ => new object());

    public IReadOnlyList<ProfileInfo> ListProfiles()
    {
        lock (_manifestLock)
        {
            return Manifest.Profiles.AsReadOnly();
        }
    }

    public string GetVaultPath(Guid profileId) =>
        Path.Combine(_vaultsDirectory, $"{profileId:D}.hsv");

    public string GetProfileWorkingDirectory(Guid profileId) =>
        Path.Combine(_workingDirectory, profileId.ToString("D"));

    private string GetLockFilePath(Guid profileId) =>
        Path.Combine(_workingDirectory, $"{profileId:D}.lock");

    public ProfileInfo CreateProfile(string name, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        lock (_manifestLock)
        {
            if (Manifest.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A profile named '{name}' already exists.");
            }

            var profile = new ProfileInfo(Guid.NewGuid(), name.Trim());
            profile.CreatedAt = _clock.UtcNow;
            profile.LastUsedAt = _clock.UtcNow;
            string vaultPath = GetVaultPath(profile.Id);

            VaultManager.InitializeNewVault(vaultPath, profile.Id, password, _cryptoConfig);

            Manifest.Profiles.Add(profile);
            Manifest.Save(_manifestPath);

            return profile;
        }
    }

    public void RenameProfile(Guid profileId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        lock (_manifestLock)
        {
            var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
                ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

            if (Manifest.Profiles.Any(p => p.Id != profileId && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A profile named '{newName}' already exists.");
            }

            profile.Name = newName.Trim();
            Manifest.Save(_manifestPath);
        }
    }

    public bool IsProfileLockedOut(Guid profileId, out TimeSpan remainingTime, out bool inSecondStage)
    {
        remainingTime = TimeSpan.Zero;
        inSecondStage = false;

        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return false;

        lock (GetAuthLock(profileId))
        {
            DateTimeOffset now = _clock.UtcNow;

            if (profile.LockoutUntil.HasValue)
            {
                // Detect clock rolled backward
                if (profile.LastAttemptAt.HasValue && now < profile.LastAttemptAt.Value)
                {
                    var delta = profile.LastAttemptAt.Value - now;
                    profile.LockoutUntil = profile.LockoutUntil.Value.Add(delta);
                    profile.LastAttemptAt = now;
                    lock (_manifestLock)
                    {
                        Manifest.Save(_manifestPath);
                    }
                }

                if (now < profile.LockoutUntil.Value)
                {
                    remainingTime = profile.LockoutUntil.Value - now;
                    inSecondStage = profile.SecondStageActive;
                    return true;
                }
                else
                {
                    // Lockout period has elapsed -> enter second stage
                    profile.SecondStageActive = true;
                    profile.LockoutUntil = null;
                    lock (_manifestLock)
                    {
                        Manifest.Save(_manifestPath);
                    }
                    inSecondStage = true;
                    return false;
                }
            }

            inSecondStage = profile.SecondStageActive;
            return false;
        }
    }

    public int GetRemainingAttempts(Guid profileId, out bool inSecondStage)
    {
        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
        {
            inSecondStage = false;
            return 0;
        }

        lock (GetAuthLock(profileId))
        {
            inSecondStage = profile.SecondStageActive;
            if (profile.SecondStageActive)
            {
                return Math.Max(0, 5 - profile.SecondStageFailedAttempts);
            }
            else
            {
                return Math.Max(0, 3 - profile.FailedAttempts);
            }
        }
    }

    /// <summary>
    /// Validates an authentication attempt against the two-stage attempt policy and vault.
    /// Thread-safe and atomic across restarts and concurrent requests.
    /// </summary>
    public AuthenticationResult ValidatePasswordAttempt(Guid profileId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        lock (GetAuthLock(profileId))
        {
            DateTimeOffset now = _clock.UtcNow;

            if (profile.IsTerminallyDestroyed)
            {
                throw new TerminalDestructionException(profileId);
            }

            // Check lockout state
            if (profile.LockoutUntil.HasValue)
            {
                if (profile.LastAttemptAt.HasValue && now < profile.LastAttemptAt.Value)
                {
                    var delta = profile.LastAttemptAt.Value - now;
                    profile.LockoutUntil = profile.LockoutUntil.Value.Add(delta);
                    profile.LastAttemptAt = now;
                    lock (_manifestLock)
                    {
                        Manifest.Save(_manifestPath);
                    }
                }

                if (now < profile.LockoutUntil.Value)
                {
                    TimeSpan remaining = profile.LockoutUntil.Value - now;
                    return new AuthenticationResult
                    {
                        Status = AuthenticationStatus.LockedOut,
                        RemainingAttempts = 0,
                        InSecondStage = profile.SecondStageActive,
                        RemainingLockout = remaining,
                        Message = $"Profile is temporarily locked out. Remaining time: {(int)remaining.TotalMinutes}m {remaining.Seconds}s."
                    };
                }
                else
                {
                    // 30-minute lockout expired -> transition to Stage 2
                    profile.SecondStageActive = true;
                    profile.LockoutUntil = null;
                    lock (_manifestLock)
                    {
                        Manifest.Save(_manifestPath);
                    }
                }
            }

            string vaultPath = GetVaultPath(profileId);
            bool isValid = VaultManager.VerifyPassword(vaultPath, profileId, password);

            if (isValid)
            {
                lock (_manifestLock)
                {
                    profile.FailedAttempts = 0;
                    profile.SecondStageActive = false;
                    profile.SecondStageFailedAttempts = 0;
                    profile.LockoutUntil = null;
                    profile.LastAttemptAt = now;
                    profile.LastUsedAt = now;
                    Manifest.Save(_manifestPath);
                }

                return new AuthenticationResult
                {
                    Status = AuthenticationStatus.Success,
                    RemainingAttempts = 3,
                    InSecondStage = false,
                    Message = "Authentication successful."
                };
            }
            else
            {
                lock (_manifestLock)
                {
                    profile.LastAttemptAt = now;

                    if (!profile.SecondStageActive)
                    {
                        profile.FailedAttempts++;
                        if (profile.FailedAttempts >= 3)
                        {
                            profile.LockoutUntil = now.AddMinutes(30);
                            Manifest.Save(_manifestPath);

                            return new AuthenticationResult
                            {
                                Status = AuthenticationStatus.LockedOut,
                                RemainingAttempts = 0,
                                InSecondStage = false,
                                RemainingLockout = TimeSpan.FromMinutes(30),
                                Message = "3 consecutive failed attempts reached. Profile locked out for 30 minutes."
                            };
                        }

                        int remaining = 3 - profile.FailedAttempts;
                        Manifest.Save(_manifestPath);

                        return new AuthenticationResult
                        {
                            Status = AuthenticationStatus.InvalidPassword,
                            RemainingAttempts = remaining,
                            InSecondStage = false,
                            Message = $"Incorrect password. {remaining} attempt(s) remaining before 30-minute lockout."
                        };
                    }
                    else
                    {
                        profile.SecondStageFailedAttempts++;
                        if (profile.SecondStageFailedAttempts >= 5)
                        {
                            // Terminal destruction on 5th stage 2 failure
                            Manifest.Save(_manifestPath);
                            ExecuteTerminalDestruction(profileId);

                            return new AuthenticationResult
                            {
                                Status = AuthenticationStatus.TerminalDestruction,
                                RemainingAttempts = 0,
                                InSecondStage = true,
                                Message = "Profile permanently destroyed due to exceeding 5 second-stage failed attempts."
                            };
                        }

                        int remaining = 5 - profile.SecondStageFailedAttempts;
                        Manifest.Save(_manifestPath);

                        return new AuthenticationResult
                        {
                            Status = AuthenticationStatus.InvalidPassword,
                            RemainingAttempts = remaining,
                            InSecondStage = true,
                            Message = $"Incorrect password in Stage 2. WARNING: {remaining} attempt(s) remaining before PERMANENT DELETION."
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Authenticates password against profile, throwing appropriate domain exceptions on failure or lockout.
    /// </summary>
    public AuthenticationResult Authenticate(Guid profileId, string password)
    {
        var result = ValidatePasswordAttempt(profileId, password);
        if (result.Status == AuthenticationStatus.LockedOut)
        {
            throw new ProfileLockedOutException(result.RemainingLockout ?? TimeSpan.FromMinutes(30), result.InSecondStage);
        }
        if (result.Status == AuthenticationStatus.TerminalDestruction)
        {
            throw new TerminalDestructionException(profileId);
        }
        if (result.Status == AuthenticationStatus.InvalidPassword)
        {
            throw new AuthenticationFailedException(
                result.Message ?? "Invalid password.",
                result.RemainingAttempts,
                result.InSecondStage,
                isLockedOut: false,
                remainingLockout: null,
                isTerminalDestruction: false);
        }
        return result;
    }

    /// <summary>
    /// Permanently deletes an affected profile's vault (.hsv), backup (.bak), temporary files (.tmp),
    /// working directory, and crash recovery directories, and commits updated manifest.
    /// </summary>
    public void ExecuteTerminalDestruction(Guid profileId)
    {
        lock (GetAuthLock(profileId))
        {
            // 1. Terminate/release active lock stream if held
            if (_activeLocks.TryRemove(profileId, out var lockStream))
            {
                try { lockStream.Dispose(); } catch { }
            }

            string lockPath = GetLockFilePath(profileId);
            SecureDeleteFile(lockPath);

            // 2. Overwrite and delete primary vault, backup, and temporary vault
            string vaultPath = GetVaultPath(profileId);
            SecureDeleteFile(vaultPath);
            SecureDeleteFile(vaultPath + ".bak");
            SecureDeleteFile(vaultPath + ".tmp");

            // 3. Purge working directory
            string workingDir = GetProfileWorkingDirectory(profileId);
            VaultManager.CleanDirectorySafely(workingDir);

            // 4. Purge all crash recovery directories for this profile
            if (Directory.Exists(_workingDirectory))
            {
                try
                {
                    var di = new DirectoryInfo(_workingDirectory);
                    foreach (var crashDir in di.GetDirectories($"{profileId:D}_crash_*"))
                    {
                        VaultManager.CleanDirectorySafely(crashDir.FullName);
                    }
                }
                catch { }
            }

            // 5. Remove profile from manifest atomically
            lock (_manifestLock)
            {
                var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId);
                if (profile != null)
                {
                    profile.IsTerminallyDestroyed = true;
                    Manifest.Profiles.Remove(profile);
                    Manifest.Save(_manifestPath);
                }
            }
        }
    }

    private static void SecureDeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var fileInfo = new FileInfo(path);
            long length = fileInfo.Length;
            if (length > 0)
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] zeros = new byte[Math.Min(4096, (int)length)];
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(zeros.Length, remaining);
                        fs.Write(zeros, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(flushToDisk: true);
                }
            }
            File.Delete(path);
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void DeleteProfile(Guid profileId, string password)
    {
        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        if (IsProfileLocked(profileId))
        {
            throw new ProfileAlreadyLockedException("Cannot delete profile while it is active or locked.");
        }

        if (IsProfileLockedOut(profileId, out var remainingTime, out var inSecondStage))
        {
            throw new ProfileLockedOutException(remainingTime, inSecondStage);
        }

        var authResult = ValidatePasswordAttempt(profileId, password);
        if (authResult.Status == AuthenticationStatus.LockedOut)
        {
            throw new ProfileLockedOutException(authResult.RemainingLockout ?? TimeSpan.FromMinutes(30), authResult.InSecondStage);
        }
        if (authResult.Status == AuthenticationStatus.TerminalDestruction)
        {
            throw new TerminalDestructionException(profileId);
        }
        if (authResult.Status == AuthenticationStatus.InvalidPassword)
        {
            throw new AuthenticationFailedException(
                authResult.Message ?? "Invalid password.",
                authResult.RemainingAttempts,
                authResult.InSecondStage);
        }

        // Successfully authenticated - permanently destroy/delete profile
        ExecuteTerminalDestruction(profileId);
    }

    public bool IsProfileLocked(Guid profileId)
    {
        if (_activeLocks.ContainsKey(profileId)) return true;

        string lockPath = GetLockFilePath(profileId);
        if (!File.Exists(lockPath)) return false;

        try
        {
            using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private FileStream AcquireProfileLock(Guid profileId)
    {
        string lockPath = GetLockFilePath(profileId);
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!_activeLocks.TryAdd(profileId, stream))
            {
                stream.Dispose();
                throw new ProfileAlreadyLockedException($"Profile '{profileId}' is already active.");
            }
            return stream;
        }
        catch (IOException)
        {
            throw new ProfileAlreadyLockedException($"Profile '{profileId}' is already locked by another process or session.");
        }
    }

    private void ReleaseProfileLock(Guid profileId)
    {
        if (_activeLocks.TryRemove(profileId, out var stream))
        {
            stream.Dispose();
            string lockPath = GetLockFilePath(profileId);
            try { File.Delete(lockPath); } catch { }
        }
    }

    /// <summary>
    /// Unlocks the profile with password, launches Helium browser, and manages session lifecycle.
    /// When the browser process exits, automatically re-encrypts the working directory and locks the vault.
    /// </summary>
    public async Task<ProfileSession> UnlockAndLaunchAsync(
        Guid profileId,
        string password,
        string? customHeliumExe = null,
        Action? onSessionCompleted = null,
        CancellationToken ct = default)
    {
        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        if (IsProfileLocked(profileId))
        {
            throw new ProfileAlreadyLockedException($"Profile '{profileId}' is already active.");
        }

        // Enforce two-stage authentication attempt policy
        var authResult = ValidatePasswordAttempt(profileId, password);
        if (authResult.Status == AuthenticationStatus.LockedOut)
        {
            throw new ProfileLockedOutException(authResult.RemainingLockout ?? TimeSpan.FromMinutes(30), authResult.InSecondStage);
        }
        if (authResult.Status == AuthenticationStatus.TerminalDestruction)
        {
            throw new TerminalDestructionException(profileId);
        }
        if (authResult.Status == AuthenticationStatus.InvalidPassword)
        {
            throw new AuthenticationFailedException(
                authResult.Message ?? "Invalid password.",
                authResult.RemainingAttempts,
                authResult.InSecondStage);
        }

        FileStream lockStream = AcquireProfileLock(profileId);

        try
        {
            string vaultPath = GetVaultPath(profileId);
            string workingDir = GetProfileWorkingDirectory(profileId);

            // Crash recovery check: if workingDir already exists and is non-empty, back it up safely
            if (Directory.Exists(workingDir) && Directory.EnumerateFileSystemEntries(workingDir).Any())
            {
                string crashRecoveryDir = workingDir + $"_crash_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                Directory.Move(workingDir, crashRecoveryDir);
            }

            // Unlock and decrypt vault to working directory asynchronously
            await Task.Run(() => VaultManager.UnlockVault(vaultPath, profileId, password, workingDir), ct).ConfigureAwait(false);

            var processManager = new BrowserProcessManager(customHeliumExe);
            processManager.Launch(workingDir);

            var session = new ProfileSession(profileId, workingDir, processManager, lockStream, GetLockFilePath(profileId));

            // Background task to track browser shutdown and perform secure re-encryption
            _ = Task.Run(async () =>
            {
                try
                {
                    await processManager.WaitForExitAsync(ct: ct).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        // Commit working data back into the encrypted vault
                        VaultManager.LockVault(workingDir, profileId, password, vaultPath, _cryptoConfig, cleanWorkingDirectory: true);
                    }
                    finally
                    {
                        ReleaseProfileLock(profileId);
                        processManager.Dispose();
                        onSessionCompleted?.Invoke();
                    }
                }
            }, ct);

            return session;
        }
        catch
        {
            ReleaseProfileLock(profileId);
            throw;
        }
    }
}
