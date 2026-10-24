using HeliumSecure.Core.Guest;
using HeliumSecure.Core.Security;

namespace HeliumSecure.Tests;

public class GuestSessionTests : IDisposable
{
    private readonly string _testRoot;

    public GuestSessionTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "HS_GuestTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void CleanupOrphanSessions_RemovesOldGuestDirectories()
    {
        var manager = new GuestSessionManager(_testRoot);

        // Create orphan session directories
        string orphan1 = Path.Combine(_testRoot, "Guest_orphan1");
        string orphan2 = Path.Combine(_testRoot, "Guest_orphan2");
        Directory.CreateDirectory(orphan1);
        Directory.CreateDirectory(orphan2);
        File.WriteAllText(Path.Combine(orphan1, "temp_cache.bin"), "orphan data");

        Assert.True(Directory.Exists(orphan1));
        Assert.True(Directory.Exists(orphan2));

        manager.CleanupOrphanSessions();

        Assert.False(Directory.Exists(orphan1));
        Assert.False(Directory.Exists(orphan2));
    }

    [Fact]
    public void NtfsSecurityHelper_SetsPermissionsWithoutCrashing()
    {
        string dirPath = Path.Combine(_testRoot, "SecureTestDir");
        NtfsSecurityHelper.SecureDirectory(dirPath);

        Assert.True(Directory.Exists(dirPath));

        string filePath = Path.Combine(dirPath, "secret.txt");
        File.WriteAllText(filePath, "test data");
        NtfsSecurityHelper.SecureFile(filePath);

        Assert.True(File.Exists(filePath));
    }
}
