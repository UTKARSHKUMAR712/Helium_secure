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

    [JsonPropertyName("failedAttempts")]
    public int FailedAttempts { get; set; } = 0;

    [JsonPropertyName("lockoutUntil")]
    public DateTimeOffset? LockoutUntil { get; set; }

    [JsonPropertyName("secondStageActive")]
    public bool SecondStageActive { get; set; } = false;

    [JsonPropertyName("secondStageFailedAttempts")]
    public int SecondStageFailedAttempts { get; set; } = 0;

    [JsonPropertyName("lastAttemptAt")]
    public DateTimeOffset? LastAttemptAt { get; set; }

    [JsonPropertyName("isTerminallyDestroyed")]
    public bool IsTerminallyDestroyed { get; set; } = false;

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
