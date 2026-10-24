using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Security;

namespace HeliumSecure.Core.Vault;

public sealed class VaultManager
{
    private static byte[] GetAad(Guid profileId) =>
        Encoding.UTF8.GetBytes($"HELIUMLOCK:{profileId:D}:v1");

    /// <summary>
    /// Creates a newly initialized encrypted vault file for a profile.
    /// </summary>
    public static void InitializeNewVault(string vaultPath, Guid profileId, string password, VaultCryptoConfig? config = null)
    {
        config ??= VaultCryptoConfig.Default;
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        string? parent = Path.GetDirectoryName(vaultPath);
        if (!string.IsNullOrEmpty(parent))
        {
            NtfsSecurityHelper.SecureDirectory(parent);
        }

        // Create empty directory structure archive
        byte[] archiveBytes;
        using (var mem = new MemoryStream())
        {
            using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("Default/Preferences");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("{}");
            }
            archiveBytes = mem.ToArray();
        }

        byte[] salt = Argon2Helper.GenerateSalt(config.SaltSizeBytes);
        byte[] key = Argon2Helper.DeriveKey(password, salt, config);
        try
        {
            var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(
                archiveBytes,
                key,
                GetAad(profileId),
                config.NonceSizeBytes,
                config.TagSizeBytes);

            var header = new VaultFileHeader
            {
                Version = VaultFileHeader.CurrentVersion,
                Salt = salt,
                MemorySizeKb = config.MemorySizeKb,
                Iterations = config.Iterations,
                DegreeOfParallelism = config.DegreeOfParallelism,
                Nonce = nonce,
                Tag = tag,
                CiphertextLength = ciphertext.Length
            };

            string tmpPath = vaultPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(fs))
            {
                header.WriteTo(writer);
                writer.Write(ciphertext);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, vaultPath, overwrite: true);
            NtfsSecurityHelper.SecureFile(vaultPath);
        }
        finally
        {
            AesGcmVaultCrypto.Wipe(key);
            AesGcmVaultCrypto.Wipe(archiveBytes);
        }
    }

    /// <summary>
    /// Unlocks and extracts the vault into the target working directory.
    /// Automatically attempts recovery from .bak if the primary vault file is corrupted or missing.
    /// </summary>
    public static void UnlockVault(string vaultPath, Guid profileId, string password, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string bakPath = vaultPath + ".bak";

        try
        {
            UnlockVaultFile(vaultPath, profileId, password, destinationDirectory);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or EndOfStreamException or FileNotFoundException)
        {
            // If primary vault is missing or corrupted, check if a valid .bak exists
            if (File.Exists(bakPath))
            {
                try
                {
                    UnlockVaultFile(bakPath, profileId, password, destinationDirectory);

                    // Recovery succeeded from .bak! Restore .bak to primary
                    File.Copy(bakPath, vaultPath, overwrite: true);
                    NtfsSecurityHelper.SecureFile(vaultPath);
                    return;
                }
                catch
                {
                    // If .bak also fails or password was wrong, throw original exception
                    throw;
                }
            }

            throw;
        }
    }

    private static void UnlockVaultFile(string filePath, Guid profileId, string password, string destinationDirectory)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Vault file not found at '{filePath}'.");
        }

        VaultFileHeader header;
        byte[] ciphertext;

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs))
        {
            header = VaultFileHeader.ReadFrom(reader);
            ciphertext = reader.ReadBytes((int)header.CiphertextLength);
            if (ciphertext.Length != header.CiphertextLength)
            {
                throw new InvalidDataException("Vault file ended unexpectedly.");
            }
        }

        var config = new VaultCryptoConfig(
            SaltSizeBytes: header.Salt.Length,
            Iterations: header.Iterations,
            MemorySizeKb: header.MemorySizeKb,
            DegreeOfParallelism: header.DegreeOfParallelism,
            KeySizeBytes: 32,
            NonceSizeBytes: header.Nonce.Length,
            TagSizeBytes: header.Tag.Length
        );

        byte[] key = Argon2Helper.DeriveKey(password, header.Salt, config);
        byte[]? plaintext = null;

        try
        {
            plaintext = AesGcmVaultCrypto.Decrypt(
                ciphertext,
                key,
                header.Nonce,
                header.Tag,
                GetAad(profileId)
            );

            // Plaintext decrypted and authenticated successfully.
            NtfsSecurityHelper.SecureDirectory(destinationDirectory);

            using var mem = new MemoryStream(plaintext);
            using var zip = new ZipArchive(mem, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                // Prevent path traversal
                if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName))
                {
                    continue;
                }

                string targetPath = Path.Combine(destinationDirectory, entry.FullName);
                string? entryDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    entry.ExtractToFile(targetPath, overwrite: true);
                }
            }
        }
        finally
        {
            AesGcmVaultCrypto.Wipe(key);
            AesGcmVaultCrypto.Wipe(plaintext);
        }
    }

    /// <summary>
    /// Archives and encrypts the working directory into the vault using atomic swap and safe recovery.
    /// Cleans up the working directory afterwards.
    /// </summary>
    public static void LockVault(
        string workingDirectory,
        Guid profileId,
        string password,
        string vaultPath,
        VaultCryptoConfig? config = null,
        bool cleanWorkingDirectory = true)
    {
        config ??= VaultCryptoConfig.Default;
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory not found: {workingDirectory}");
        }

        byte[] archiveBytes;
        using (var mem = new MemoryStream())
        {
            using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddDirectoryToZip(zip, workingDirectory, "");
            }
            archiveBytes = mem.ToArray();
        }

        byte[] salt = Argon2Helper.GenerateSalt(config.SaltSizeBytes);
        byte[] key = Argon2Helper.DeriveKey(password, salt, config);

        try
        {
            var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(
                archiveBytes,
                key,
                GetAad(profileId),
                config.NonceSizeBytes,
                config.TagSizeBytes);

            var header = new VaultFileHeader
            {
                Version = VaultFileHeader.CurrentVersion,
                Salt = salt,
                MemorySizeKb = config.MemorySizeKb,
                Iterations = config.Iterations,
                DegreeOfParallelism = config.DegreeOfParallelism,
                Nonce = nonce,
                Tag = tag,
                CiphertextLength = ciphertext.Length
            };

            string tmpPath = vaultPath + ".tmp";
            string bakPath = vaultPath + ".bak";

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(fs))
            {
                header.WriteTo(writer);
                writer.Write(ciphertext);
                fs.Flush(flushToDisk: true);
            }

            // Verify the newly written tmp file before touching the original
            using (var fs = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                var verifiedHeader = VaultFileHeader.ReadFrom(reader);
                if (verifiedHeader.CiphertextLength != ciphertext.Length)
                {
                    throw new InvalidDataException("Verification of written temporary vault failed.");
                }
            }

            // Safe atomic swap: primary -> bak, tmp -> primary
            if (File.Exists(vaultPath))
            {
                File.Move(vaultPath, bakPath, overwrite: true);
            }

            File.Move(tmpPath, vaultPath, overwrite: true);
            NtfsSecurityHelper.SecureFile(vaultPath);

            // Once committed and secured, clean the bak file
            if (File.Exists(bakPath))
            {
                try { File.Delete(bakPath); } catch { /* best effort */ }
            }

            // Shred/clean working directory if requested
            if (cleanWorkingDirectory)
            {
                CleanDirectorySafely(workingDirectory);
            }
        }
        finally
        {
            AesGcmVaultCrypto.Wipe(key);
            AesGcmVaultCrypto.Wipe(archiveBytes);
        }
    }

    private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string relativeBase)
    {
        var dirInfo = new DirectoryInfo(sourceDir);
        foreach (var file in dirInfo.GetFiles())
        {
            // Skip lock files or temporary vault files inside working dir
            if (file.Name.Equals(".lock", StringComparison.OrdinalIgnoreCase) ||
                file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string entryName = string.IsNullOrEmpty(relativeBase)
                ? file.Name
                : Path.Combine(relativeBase, file.Name).Replace('\\', '/');

            zip.CreateEntryFromFile(file.FullName, entryName, CompressionLevel.Optimal);
        }

        foreach (var subDir in dirInfo.GetDirectories())
        {
            string newBase = string.IsNullOrEmpty(relativeBase)
                ? subDir.Name
                : Path.Combine(relativeBase, subDir.Name).Replace('\\', '/');

            AddDirectoryToZip(zip, subDir.FullName, newBase);
        }
    }

    public static void CleanDirectorySafely(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    // Overwrite file contents with zeros before deleting to prevent recovery
                    using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        byte[] zeros = new byte[Math.Min(4096, (int)fs.Length)];
                        long remaining = fs.Length;
                        while (remaining > 0)
                        {
                            int toWrite = (int)Math.Min(zeros.Length, remaining);
                            fs.Write(zeros, 0, toWrite);
                            remaining -= toWrite;
                        }
                    }
                    file.Delete();
                }
                catch
                {
                    // If locked or fails, still try to delete
                    try { file.Delete(); } catch { }
                }
            }

            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
