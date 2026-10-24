using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Security;
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
    private readonly ConcurrentDictionary<Guid, FileStream> _activeLocks = new();

    public ProfileManifest Manifest { get; private set; }

    public ProfileManager(string? customBaseDirectory = null, VaultCryptoConfig? cryptoConfig = null)
    {
        _cryptoConfig = cryptoConfig ?? VaultCryptoConfig.Default;

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

    public IReadOnlyList<ProfileInfo> ListProfiles()
    {
        return Manifest.Profiles.AsReadOnly();
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

        if (Manifest.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A profile named '{name}' already exists.");
        }

        var profile = new ProfileInfo(Guid.NewGuid(), name.Trim());
        string vaultPath = GetVaultPath(profile.Id);

        VaultManager.InitializeNewVault(vaultPath, profile.Id, password, _cryptoConfig);

        Manifest.Profiles.Add(profile);
        Manifest.Save(_manifestPath);

        return profile;
    }

    public void RenameProfile(Guid profileId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        if (Manifest.Profiles.Any(p => p.Id != profileId && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A profile named '{newName}' already exists.");
        }

        profile.Name = newName.Trim();
        Manifest.Save(_manifestPath);
    }

    public void DeleteProfile(Guid profileId, string password)
    {
        var profile = Manifest.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        if (IsProfileLocked(profileId))
        {
            throw new ProfileAlreadyLockedException("Cannot delete profile while it is active or locked.");
        }

        string vaultPath = GetVaultPath(profileId);
        string workingDir = GetProfileWorkingDirectory(profileId);

        // Verify password first by attempting decryption into a temporary check or verify
        string testCheckDir = Path.Combine(Path.GetTempPath(), $"hs_check_{Guid.NewGuid():N}");
        try
        {
            VaultManager.UnlockVault(vaultPath, profileId, password, testCheckDir);
        }
        finally
        {
            VaultManager.CleanDirectorySafely(testCheckDir);
        }

        // Delete vault file and any backup
        if (File.Exists(vaultPath)) File.Delete(vaultPath);
        if (File.Exists(vaultPath + ".bak")) File.Delete(vaultPath + ".bak");
        if (File.Exists(vaultPath + ".tmp")) File.Delete(vaultPath + ".tmp");

        // Clean working directory
        VaultManager.CleanDirectorySafely(workingDir);

        Manifest.Profiles.Remove(profile);
        Manifest.Save(_manifestPath);
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

            profile.LastUsedAt = DateTimeOffset.UtcNow;
            Manifest.Save(_manifestPath);

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
