using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace HeliumSecure.Core.Crypto;

public static class Argon2Helper
{
    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    public static byte[] GenerateSalt(int sizeBytes = 32)
    {
        return RandomNumberGenerator.GetBytes(sizeBytes);
    }

    /// <summary>
    /// Generates a cryptographically secure random nonce for AES-GCM (12 bytes).
    /// </summary>
    public static byte[] GenerateNonce(int sizeBytes = 12)
    {
        return RandomNumberGenerator.GetBytes(sizeBytes);
    }

    /// <summary>
    /// Derives an encryption key from a password and salt using Argon2id.
    /// Safely clears intermediate password bytes.
    /// </summary>
    public static byte[] DeriveKey(string password, byte[] salt, VaultCryptoConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(config);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = config.DegreeOfParallelism,
                MemorySize = config.MemorySizeKb,
                Iterations = config.Iterations
            };

            return argon2.GetBytes(config.KeySizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
