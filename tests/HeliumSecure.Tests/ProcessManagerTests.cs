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

    [Fact]
    public void JobObject_Create_Assign_And_Terminate_WorksCleanly()
    {
        using var job = new JobObject();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 10 > nul",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(process);

        bool assigned = job.AssignProcess(process);
        Assert.True(assigned);

        int count = job.GetActiveProcessCount();
        Assert.True(count >= 1);

        // Terminating the job terminates the attached process tree
        job.Terminate(0);
        process.WaitForExit(3000);
        Assert.True(process.HasExited);
    }
}