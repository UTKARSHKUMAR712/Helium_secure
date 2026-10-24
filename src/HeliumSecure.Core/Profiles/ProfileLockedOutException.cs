namespace HeliumSecure.Core.Profiles;

public class ProfileLockedOutException : InvalidOperationException
{
    public TimeSpan RemainingLockout { get; }
    public bool InSecondStage { get; }

    public ProfileLockedOutException(TimeSpan remainingLockout, bool inSecondStage)
        : base($"Profile is temporarily locked out. Remaining time: {(int)remainingLockout.TotalMinutes}m {remainingLockout.Seconds}s.")
    {
        RemainingLockout = remainingLockout;
        InSecondStage = inSecondStage;
    }
}
