using HeliumSecure.Core.Process;

namespace HeliumSecure.Tests;

public class ProcessManagerTests
{
    [Fact]
    public void ResolveHeliumExecutable_FindsInstalledHeliumOrNull()
    {
        string? resolved = BrowserProcessManager.ResolveHeliumExecutable();

        // On this machine Helium is installed in LocalAppData
        if (resolved != null)
        {
            Assert.True(File.Exists(resolved));
            Assert.EndsWith("chrome.exe", resolved, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Launch_ThrowsFileNotFoundException_WhenExecutableDoesNotExist()
    {
        var manager = new BrowserProcessManager(@"C:\NonExistentDirectory\chrome.exe");
        string tempDir = Path.Combine(Path.GetTempPath(), "test_user_data");

        Assert.Throws<FileNotFoundException>(() =>
            manager.Launch(tempDir));
    }
}
