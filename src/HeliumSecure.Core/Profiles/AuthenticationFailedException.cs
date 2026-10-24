using System.Security.Cryptography;

namespace HeliumSecure.Core.Profiles;

public class AuthenticationFailedException : CryptographicException
{
    public int RemainingAttempts { get; }
    public bool InSecondStage { get; }
    public bool IsLockedOut { get; }
    public TimeSpan? RemainingLockout { get; }
    public bool IsTerminalDestruction { get; }

    public AuthenticationFailedException(
        string message,
        int remainingAttempts,
        bool inSecondStage,
        bool isLockedOut = false,
        TimeSpan? remainingLockout = null,
        bool isTerminalDestruction = false)
        : base(message)
    {
        RemainingAttempts = remainingAttempts;
        InSecondStage = inSecondStage;
        IsLockedOut = isLockedOut;
        RemainingLockout = remainingLockout;
        IsTerminalDestruction = isTerminalDestruction;
    }
}
