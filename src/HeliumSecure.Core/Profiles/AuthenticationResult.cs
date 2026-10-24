namespace HeliumSecure.Core.Profiles;

public enum AuthenticationStatus
{
    Success,
    InvalidPassword,
    LockedOut,
    TerminalDestruction
}

public sealed class AuthenticationResult
{
    public bool Success => Status == AuthenticationStatus.Success;
    public AuthenticationStatus Status { get; set; }
    public int RemainingAttempts { get; set; }
    public bool InSecondStage { get; set; }
    public TimeSpan? RemainingLockout { get; set; }
    public string? Message { get; set; }
}
