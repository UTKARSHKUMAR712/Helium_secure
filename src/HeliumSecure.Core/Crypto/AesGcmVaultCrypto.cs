using System.Security.Cryptography;

namespace HeliumSecure.Core.Crypto;

public static class AesGcmVaultCrypto
{
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// </summary>
    public static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
        byte[] plaintext,
        byte[] key,
        byte[]? associatedData = null,
        int nonceSize = 12,
        int tagSize = 16)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(key));
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(nonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[tagSize];

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return (ciphertext, nonce, tag);
    }

    /// <summary>
    /// Decrypts ciphertext using AES-256-GCM. Throws CryptographicException on tag mismatch or tampering.
    /// </summary>
    public static byte[] Decrypt(
        byte[] ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] tag,
        byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(tag);

        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(key));
        }

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    /// <summary>
    /// Zeroes out the memory in the specified buffer.
    /// </summary>
    public static void Wipe(byte[]? buffer)
    {
        if (buffer != null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
