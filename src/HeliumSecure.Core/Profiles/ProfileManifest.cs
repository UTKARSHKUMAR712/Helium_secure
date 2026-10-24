using System.Text.Json;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Security;

namespace HeliumSecure.Core.Profiles;

public sealed class ProfileManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<ProfileInfo> Profiles { get; set; } = [];

    public static ProfileManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (!File.Exists(manifestPath))
        {
            return new ProfileManifest();
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ProfileManifest>(json, JsonOptions);
            return manifest ?? new ProfileManifest();
        }
        catch (JsonException)
        {
            // If corrupted, return new or fallback
            return new ProfileManifest();
        }
    }

    public void Save(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        string? dir = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(dir))
        {
            NtfsSecurityHelper.SecureDirectory(dir);
        }

        string tmpPath = manifestPath + ".tmp";
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(tmpPath, json);
        NtfsSecurityHelper.SecureFile(tmpPath);

        File.Move(tmpPath, manifestPath, overwrite: true);
        NtfsSecurityHelper.SecureFile(manifestPath);
    }
}
