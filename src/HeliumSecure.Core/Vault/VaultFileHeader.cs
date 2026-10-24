using System.Text;

namespace HeliumSecure.Core.Vault;

public sealed class VaultFileHeader
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("HSVAULT1");
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public byte[] Salt { get; set; } = [];
    public int MemorySizeKb { get; set; }
    public int Iterations { get; set; }
    public int DegreeOfParallelism { get; set; }
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public long CiphertextLength { get; set; }

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(Salt.Length);
        writer.Write(Salt);
        writer.Write(MemorySizeKb);
        writer.Write(Iterations);
        writer.Write(DegreeOfParallelism);
        writer.Write(Nonce.Length);
        writer.Write(Nonce);
        writer.Write(Tag.Length);
        writer.Write(Tag);
        writer.Write(CiphertextLength);
    }

    public static VaultFileHeader ReadFrom(BinaryReader reader)
    {
        byte[] magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid Helium Secure Vault magic header.");
        }

        int version = reader.ReadInt32();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported vault version: {version}");
        }

        int saltLen = reader.ReadInt32();
        if (saltLen < 16 || saltLen > 128)
        {
            throw new InvalidDataException($"Invalid salt length: {saltLen}");
        }
        byte[] salt = reader.ReadBytes(saltLen);

        int mem = reader.ReadInt32();
        int iter = reader.ReadInt32();
        int par = reader.ReadInt32();

        int nonceLen = reader.ReadInt32();
        if (nonceLen != 12)
        {
            throw new InvalidDataException($"Invalid nonce length: {nonceLen}");
        }
        byte[] nonce = reader.ReadBytes(nonceLen);

        int tagLen = reader.ReadInt32();
        if (tagLen != 16)
        {
            throw new InvalidDataException($"Invalid authentication tag length: {tagLen}");
        }
        byte[] tag = reader.ReadBytes(tagLen);

        long ctLen = reader.ReadInt64();
        if (ctLen < 0)
        {
            throw new InvalidDataException($"Invalid ciphertext length: {ctLen}");
        }

        return new VaultFileHeader
        {
            Version = version,
            Salt = salt,
            MemorySizeKb = mem,
            Iterations = iter,
            DegreeOfParallelism = par,
            Nonce = nonce,
            Tag = tag,
            CiphertextLength = ctLen
        };
    }
}
