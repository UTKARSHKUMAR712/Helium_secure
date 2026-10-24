namespace HeliumSecure.Core.Crypto;

public sealed record VaultCryptoConfig(
    int SaltSizeBytes = 32,
    int Iterations = 3,
    int MemorySizeKb = 65536,
    int DegreeOfParallelism = 4,
    int KeySizeBytes = 32,
    int NonceSizeBytes = 12,
    int TagSizeBytes = 16)
{
    public static VaultCryptoConfig Default { get; } = new(
        SaltSizeBytes: 32,
        Iterations: 3,
        MemorySizeKb: 65536,
        DegreeOfParallelism: 4,
        KeySizeBytes: 32,
        NonceSizeBytes: 12,
        TagSizeBytes: 16
    );

    /// <summary>
    /// Lightweight configuration for fast automated testing.
    /// </summary>
    public static VaultCryptoConfig FastTest { get; } = new(
        SaltSizeBytes: 32,
        Iterations: 1,
        MemorySizeKb: 1024,
        DegreeOfParallelism: 1,
        KeySizeBytes: 32,
        NonceSizeBytes: 12,
        TagSizeBytes: 16
    );
}
