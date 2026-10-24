using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HeliumSecure.Core.Security;

public static class NtfsSecurityHelper
{
    /// <summary>
    /// Ensures the directory exists and restricts NTFS access control permissions exclusively
    /// to the current Windows user and SYSTEM. Disables inherited permissions.
    /// </summary>
    public static void SecureDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var directorySecurity = new DirectorySecurity();

            // Disable inheritance and preserve no inherited rules
            directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                var userRule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);
                directorySecurity.AddAccessRule(userRule);
            }

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var systemRule = new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            directorySecurity.AddAccessRule(systemRule);

            directoryInfo.SetAccessControl(directorySecurity);
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback for non-NTFS or non-Windows test environments
        }
        catch (UnauthorizedAccessException)
        {
            // If caller lacks permission to modify DACL, fail soft or log
        }
    }

    /// <summary>
    /// Restricts permissions on a single file so only the current user and SYSTEM can access it.
    /// </summary>
    public static void SecureFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileSecurity = new FileSecurity();

            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                var userRule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow);
                fileSecurity.AddAccessRule(userRule);
            }

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var systemRule = new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow);
            fileSecurity.AddAccessRule(systemRule);

            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
