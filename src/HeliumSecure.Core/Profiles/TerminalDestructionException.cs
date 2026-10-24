namespace HeliumSecure.Core.Profiles;

public class TerminalDestructionException : InvalidOperationException
{
    public Guid ProfileId { get; }

    public TerminalDestructionException(Guid profileId)
        : base($"Profile '{profileId}' exceeded maximum authentication attempts. All vault data, backups, and profile files have been permanently destroyed.")
    {
        ProfileId = profileId;
    }
}
