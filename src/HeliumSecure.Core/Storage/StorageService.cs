using System.IO;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Core.Storage;

public static class StorageService
{
    /// <summary>
    /// Calculates the total size of files in a directory recursively without following reparse points.
    /// </summary>
    public static long CalculateDirectorySize(string directoryPath, out bool isComplete)
    {
        isComplete = true;
        if (!Directory.Exists(directoryPath)) return 0;

        long totalBytes = 0;
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(directoryPath);

        while (dirQueue.Count > 0)
        {
            string currentDir = dirQueue.Dequeue();

            try
            {
                var dirInfo = new DirectoryInfo(currentDir);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) && currentDir != directoryPath)
                {
                    continue; // Skip symlinks and junctions
                }

                foreach (var file in dirInfo.GetFiles())
                {
                    try
                    {
                        totalBytes += file.Length;
                    }
                    catch
                    {
                        isComplete = false;
                    }
                }

                foreach (var subDir in dirInfo.GetDirectories())
                {
                    if (!subDir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        dirQueue.Enqueue(subDir.FullName);
                    }
                }
            }
            catch
            {
                isComplete = false;
            }
        }

        return totalBytes;
    }

    public static long GetFileSize(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                return new FileInfo(filePath).Length;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// Computes accurate disk storage overview for installed application, user data, and total footprint.
    /// </summary>
    public static StorageOverviewModel GetStorageOverview(string? customBaseDir = null, string? customAppDir = null)
    {
        string appDir = customAppDir ?? AppContext.BaseDirectory;
        string baseDataDir = customBaseDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliumSecure");

        bool allMeasurementsComplete = true;

        // 1. Installed Application Size
        long totalAppSize = CalculateDirectorySize(appDir, out bool appComplete);
        if (!appComplete) allMeasurementsComplete = false;

        // 2. Bundled Browser Size
        string bundledBrowserDir = Path.Combine(appDir, "browser");
        long bundledBrowserSize = 0;
        if (Directory.Exists(bundledBrowserDir))
        {
            bundledBrowserSize = CalculateDirectorySize(bundledBrowserDir, out bool browserComplete);
            if (!browserComplete) allMeasurementsComplete = false;
        }

        // Launcher size is app size minus bundled browser
        long launcherSize = Math.Max(0, totalAppSize - bundledBrowserSize);

        // 3. Vaults and Backups Size
        string vaultsDir = Path.Combine(baseDataDir, "Vaults");
        long vaultsAndBackupsSize = 0;
        if (Directory.Exists(vaultsDir))
        {
            vaultsAndBackupsSize = CalculateDirectorySize(vaultsDir, out bool vaultsComplete);
            if (!vaultsComplete) allMeasurementsComplete = false;
        }

        // 4. Temporary and Crash Recovery Data
        long tempAndCrashSize = 0;

        // Guest sessions
        string guestDir = Path.Combine(baseDataDir, "GuestSessions");
        if (Directory.Exists(guestDir))
        {
            tempAndCrashSize += CalculateDirectorySize(guestDir, out bool guestComplete);
            if (!guestComplete) allMeasurementsComplete = false;
        }

        // Crash directories in Profiles/
        string profilesDir = Path.Combine(baseDataDir, "Profiles");
        long activeProfilesWorkingDirSize = 0;

        if (Directory.Exists(profilesDir))
        {
            try
            {
                var dirInfo = new DirectoryInfo(profilesDir);
                foreach (var dir in dirInfo.GetDirectories())
                {
                    if (dir.Name.Contains("_crash_"))
                    {
                        tempAndCrashSize += CalculateDirectorySize(dir.FullName, out bool crashComplete);
                        if (!crashComplete) allMeasurementsComplete = false;
                    }
                    else
                    {
                        activeProfilesWorkingDirSize += CalculateDirectorySize(dir.FullName, out bool workComplete);
                        if (!workComplete) allMeasurementsComplete = false;
                    }
                }
            }
            catch
            {
                allMeasurementsComplete = false;
            }
        }

        // Total profile data = Vaults & Backups + Working Dirs + Crash Dirs
        long totalProfilesData = vaultsAndBackupsSize + activeProfilesWorkingDirSize + tempAndCrashSize;

        // Combined managed footprint = App Size + User Data
        long combinedFootprint = totalAppSize + totalProfilesData;

        return new StorageOverviewModel
        {
            InstalledAppSizeBytes = totalAppSize,
            BundledBrowserSizeBytes = bundledBrowserSize,
            LauncherSizeBytes = launcherSize,
            TotalProfilesSizeBytes = totalProfilesData,
            TotalVaultsAndBackupsSizeBytes = vaultsAndBackupsSize,
            TempAndCrashRecoverySizeBytes = tempAndCrashSize,
            TotalFootprintBytes = combinedFootprint,
            IsMeasurementComplete = allMeasurementsComplete
        };
    }

    /// <summary>
    /// Computes accurate storage breakdown for a single profile.
    /// </summary>
    public static ProfileStorageModel GetProfileStorageDetails(ProfileManager pm, Guid profileId, string? customBaseDir = null)
    {
        var profile = pm.ListProfiles().FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' not found.");

        string baseDir = customBaseDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliumSecure");

        string vaultPath = pm.GetVaultPath(profileId);
        string bakPath = vaultPath + ".bak";
        string workDir = pm.GetProfileWorkingDirectory(profileId);
        string profilesDir = Path.Combine(baseDir, "Profiles");

        bool isComplete = true;

        long vaultSize = GetFileSize(vaultPath);
        long backupSize = GetFileSize(bakPath);
        long workingDirSize = CalculateDirectorySize(workDir, out bool workComplete);
        if (!workComplete) isComplete = false;

        long crashSize = 0;
        if (Directory.Exists(profilesDir))
        {
            try
            {
                var di = new DirectoryInfo(profilesDir);
                foreach (var crashDir in di.GetDirectories($"{profileId:D}_crash_*"))
                {
                    crashSize += CalculateDirectorySize(crashDir.FullName, out bool crashComplete);
                    if (!crashComplete) isComplete = false;
                }
            }
            catch
            {
                isComplete = false;
            }
        }

        long totalSize = vaultSize + backupSize + workingDirSize + crashSize;

        return new ProfileStorageModel
        {
            ProfileId = profileId,
            Name = profile.Name,
            CreatedAt = profile.CreatedAt,
            LastUsedAt = profile.LastUsedAt,
            IsLocked = pm.IsProfileLocked(profileId),
            VaultSizeBytes = vaultSize,
            BackupSizeBytes = backupSize,
            WorkingDirectorySizeBytes = workingDirSize,
            CrashRecoverySizeBytes = crashSize,
            TotalSizeBytes = totalSize,
            IsMeasurementComplete = isComplete
        };
    }

    /// <summary>
    /// Clears selected storage categories (cache, history, cookies, downloads) inside a private profile.
    /// Prevents clearing while the browser process is active.
    /// </summary>
    public static void ClearProfileStorage(
        ProfileManager pm,
        Guid profileId,
        string password,
        ClearStorageOptions options,
        VaultCryptoConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (pm.IsProfileLocked(profileId))
        {
            throw new ProfileAlreadyLockedException("Cannot clear storage while the browser session is active. Please close Helium first.");
        }

        if (pm.IsProfileLockedOut(profileId, out var remainingLockout, out var inSecondStage))
        {
            throw new ProfileLockedOutException(remainingLockout, inSecondStage);
        }

        var auth = pm.ValidatePasswordAttempt(profileId, password);
        if (auth.Status == AuthenticationStatus.LockedOut)
        {
            throw new ProfileLockedOutException(auth.RemainingLockout ?? TimeSpan.FromMinutes(30), auth.InSecondStage);
        }
        if (auth.Status == AuthenticationStatus.TerminalDestruction)
        {
            throw new TerminalDestructionException(profileId);
        }
        if (auth.Status == AuthenticationStatus.InvalidPassword)
        {
            throw new AuthenticationFailedException(auth.Message ?? "Invalid password.", auth.RemainingAttempts, auth.InSecondStage);
        }

        string vaultPath = pm.GetVaultPath(profileId);
        if (!File.Exists(vaultPath))
        {
            throw new FileNotFoundException($"Profile vault not found at '{vaultPath}'.");
        }

        // Preserve pre-existing backup if present
        string bakPath = vaultPath + ".bak";
        bool bakExisted = File.Exists(bakPath);
        string? tempBakHold = null;
        if (bakExisted)
        {
            tempBakHold = Path.Combine(Path.GetTempPath(), $"HS_BakHold_{Guid.NewGuid():N}.bak");
            File.Copy(bakPath, tempBakHold, overwrite: true);
        }

        string tempStagingDir = Path.Combine(Path.GetTempPath(), $"HS_Prune_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempStagingDir);

        try
        {
            // 1. Decrypt and unpack vault to temporary staging folder
            VaultManager.UnlockVault(vaultPath, profileId, password, tempStagingDir);

            // 2. Clear selected browser storage categories safely
            if (options.ClearCache)
            {
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Cache"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Code Cache"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "GPUCache"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "ShaderCache"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "GrShaderCache"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "ShaderCache"));
            }

            if (options.ClearHistory)
            {
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "History"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "History-journal"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "History Provider Cache"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Top Sites"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Shortcuts"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Visited Links"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Network Action Predictor"));
            }

            if (options.ClearCookiesAndSiteData)
            {
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Network", "Cookies"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Network", "Cookies-journal"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Cookies"));
                DeleteFileIfExists(Path.Combine(tempStagingDir, "Default", "Cookies-journal"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Local Storage"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Session Storage"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "IndexedDB"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Service Worker"));
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "databases"));
            }

            if (options.ClearDownloads)
            {
                DeleteDirIfExists(Path.Combine(tempStagingDir, "Default", "Download Service"));
            }

            // 3. Re-encrypt pruned directory back into the vault atomically
            VaultManager.LockVault(tempStagingDir, profileId, password, vaultPath, config, cleanWorkingDirectory: true);

            // Restore preserved backup
            if (tempBakHold != null && File.Exists(tempBakHold))
            {
                File.Copy(tempBakHold, bakPath, overwrite: true);
            }
        }
        finally
        {
            if (tempBakHold != null && File.Exists(tempBakHold))
            {
                try { File.Delete(tempBakHold); } catch { }
            }
            VaultManager.CleanDirectorySafely(tempStagingDir);
        }
    }

    /// <summary>
    /// Purges any orphan crash recovery directories associated with a profile ID.
    /// </summary>
    public static void PurgeCrashDirectories(string? customBaseDir, Guid profileId)
    {
        string baseDir = customBaseDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliumSecure");
        string profilesDir = Path.Combine(baseDir, "Profiles");

        if (Directory.Exists(profilesDir))
        {
            try
            {
                var di = new DirectoryInfo(profilesDir);
                foreach (var crashDir in di.GetDirectories($"{profileId:D}_crash_*"))
                {
                    VaultManager.CleanDirectorySafely(crashDir.FullName);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Deletes the profile and purges all associated vault, backup, working, and crash recovery directories.
    /// </summary>
    public static void DeleteProfileCompletely(ProfileManager pm, Guid profileId, string password, string? customBaseDir = null)
    {
        // Delete using profile manager (verifies password and removes vault, backup, working dir, manifest)
        pm.DeleteProfile(profileId, password);

        // Additionally clean any orphan crash recovery directories associated with this profile ID
        PurgeCrashDirectories(customBaseDir, profileId);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void DeleteDirIfExists(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}
