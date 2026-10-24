using System.Security.Cryptography;
using System.Text;
using HeliumSecure.Core.Crypto;

namespace HeliumSecure.Tests;

public class CryptoTests
{
    private readonly VaultCryptoConfig _fastConfig = VaultCryptoConfig.FastTest;

    [Fact]
    public void DeriveKey_SamePasswordAndSalt_ReturnsIdenticalKey()
    {
        byte[] salt = Argon2Helper.GenerateSalt(_fastConfig.SaltSizeBytes);
        string password = "SuperSecretPassword123!";

        byte[] key1 = Argon2Helper.DeriveKey(password, salt, _fastConfig);
        byte[] key2 = Argon2Helper.DeriveKey(password, salt, _fastConfig);

        Assert.Equal(key1, key2);
        Assert.Equal(32, key1.Length);
    }

    [Fact]
    public void DeriveKey_DifferentSalt_ReturnsDifferentKeys()
    {
        byte[] salt1 = Argon2Helper.GenerateSalt(_fastConfig.SaltSizeBytes);
        byte[] salt2 = Argon2Helper.GenerateSalt(_fastConfig.SaltSizeBytes);
        string password = "SuperSecretPassword123!";

        byte[] key1 = Argon2Helper.DeriveKey(password, salt1, _fastConfig);
        byte[] key2 = Argon2Helper.DeriveKey(password, salt2, _fastConfig);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void AesGcm_Roundtrip_EncryptAndDecrypt_Succeeds()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = Encoding.UTF8.GetBytes("Confidential Chromium Profile Preferences and State");
        byte[] aad = Encoding.UTF8.GetBytes("profile-aad-token-1");

        var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(plaintext, key, aad);
        byte[] decrypted = AesGcmVaultCrypto.Decrypt(ciphertext, key, nonce, tag, aad);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void AesGcm_TamperedCiphertext_ThrowsCryptographicException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = Encoding.UTF8.GetBytes("Confidential Data");
        byte[] aad = Encoding.UTF8.GetBytes("profile-aad-token-1");

        var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(plaintext, key, aad);

        // Tamper with one byte in the ciphertext
        ciphertext[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            AesGcmVaultCrypto.Decrypt(ciphertext, key, nonce, tag, aad));
    }

    [Fact]
    public void AesGcm_TamperedTag_ThrowsCryptographicException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = Encoding.UTF8.GetBytes("Confidential Data");
        byte[] aad = Encoding.UTF8.GetBytes("profile-aad-token-1");

        var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(plaintext, key, aad);

        // Tamper with tag
        tag[0] ^= 0xAA;

        Assert.ThrowsAny<CryptographicException>(() =>
            AesGcmVaultCrypto.Decrypt(ciphertext, key, nonce, tag, aad));
    }

    [Fact]
    public void AesGcm_MismatchedAad_ThrowsCryptographicException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = Encoding.UTF8.GetBytes("Confidential Data");
        byte[] aad1 = Encoding.UTF8.GetBytes("profile-uuid-1");
        byte[] aad2 = Encoding.UTF8.GetBytes("profile-uuid-2");

        var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(plaintext, key, aad1);

        // Attempt decrypt with different AAD (cross-profile tampering prevention)
        Assert.ThrowsAny<CryptographicException>(() =>
            AesGcmVaultCrypto.Decrypt(ciphertext, key, nonce, tag, aad2));
    }

    [Fact]
    public void AesGcm_WrongKey_ThrowsCryptographicException()
    {
        byte[] key1 = RandomNumberGenerator.GetBytes(32);
        byte[] key2 = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = Encoding.UTF8.GetBytes("Confidential Data");

        var (ciphertext, nonce, tag) = AesGcmVaultCrypto.Encrypt(plaintext, key1);

        Assert.ThrowsAny<CryptographicException>(() =>
            AesGcmVaultCrypto.Decrypt(ciphertext, key2, nonce, tag));
    }
}
