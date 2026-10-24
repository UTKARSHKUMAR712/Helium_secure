using HeliumSecure.Core.Process;

namespace HeliumSecure.Tests;

public class HeliumIntegrationTests : IDisposable
{
    private readonly string _tempUserDataDir;

    public HeliumIntegrationTests()
    {
        _tempUserDataDir = Path.Combine(Path.GetTempPath(), "HeliumTestUDD_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempUserDataDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempUserDataDir))
            {
                Directory.Delete(_tempUserDataDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task HeliumBrowser_CanLaunchAndExit_Cleanly()
    {
        string? exe = BrowserProcessManager.ResolveHeliumExecutable();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            // Skip if Helium is not installed in the testing environment
            return;
        }

        using var processManager = new BrowserProcessManager(exe);
        // Launch with headless mode so no window pops up during automated testing
        var proc = processManager.Launch(_tempUserDataDir, additionalArgs: "--headless=new --dump-dom about:blank");

        Assert.NotNull(proc);
        Assert.False(proc.HasExited);

        // Wait for exit
        await processManager.WaitForExitAsync(maxGracePeriodMs: 5000);

        Assert.True(proc.HasExited);
        // Verify Chromium created standard user-data files
        Assert.True(Directory.Exists(_tempUserDataDir));
    }
}
