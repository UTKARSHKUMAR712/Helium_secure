namespace HeliumSecure.Core.Profiles;

public class ProfileAlreadyLockedException : InvalidOperationException
{
    public ProfileAlreadyLockedException(string message) : base(message)
    {
    }
}
