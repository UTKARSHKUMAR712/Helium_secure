using System.Text.Json.Serialization;

namespace HeliumSecure.Core.Models;

public sealed class ProfileInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTimeOffset LastUsedAt { get; set; }

    [JsonIgnore]
    public bool IsActiveSession { get; set; }

    public ProfileInfo()
    {
    }

    public ProfileInfo(Guid id, string name)
    {
        Id = id;
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
        LastUsedAt = DateTimeOffset.UtcNow;
    }
}
