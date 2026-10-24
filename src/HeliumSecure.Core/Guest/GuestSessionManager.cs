using System.IO;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Security;
using HeliumSecure.Core.Vault;

namespace HeliumSecure.Core.Guest;

public sealed class GuestSession : IAsyncDisposable
{
    public string SessionDirectory { get; }
    public BrowserProcessManager ProcessManager { get; }

    public GuestSession(string sessionDirectory, BrowserProcessManager processManager)
    {
        SessionDirectory = sessionDirectory;
        ProcessManager = processManager;
    }

    public async Task WaitForExitAndCleanupAsync(CancellationToken ct = default)
    {
        try
        {
            await ProcessManager.WaitForExitAsync(ct: ct).ConfigureAwait(false);
        }
        finally
        {
            ProcessManager.Dispose();
            VaultManager.CleanDirectorySafely(SessionDirectory);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await WaitForExitAndCleanupAsync().ConfigureAwait(false);
    }
}

public sealed class GuestSessionManager
{
    public string RootGuestDirectory { get; }

    public GuestSessionManager(string? baseDir = null)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootGuestDirectory = baseDir ?? Path.Combine(localAppData, "HeliumSecure", "GuestSessions");
        NtfsSecurityHelper.SecureDirectory(RootGuestDirectory);
    }

    /// <summary>
    /// Cleans up any orphan guest session directories left over from crashes or abnormal exits.
    /// </summary>
    public void CleanupOrphanSessions()
    {
        if (!Directory.Exists(RootGuestDirectory)) return;

        try
        {
            var di = new DirectoryInfo(RootGuestDirectory);
            foreach (var dir in di.GetDirectories("Guest_*"))
            {
                VaultManager.CleanDirectorySafely(dir.FullName);
            }
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Creates a fresh disposable session, secures it with NTFS ACLs, and starts the Helium browser.
    /// </summary>
    public GuestSession StartSession(string? customExecutablePath = null, string? initialUrl = null)
    {
        CleanupOrphanSessions();

        string sessionId = Guid.NewGuid().ToString("N");
        string sessionDir = Path.Combine(RootGuestDirectory, $"Guest_{sessionId}");
        NtfsSecurityHelper.SecureDirectory(sessionDir);

        var processManager = new BrowserProcessManager(customExecutablePath);
        processManager.Launch(sessionDir, initialUrl);

        return new GuestSession(sessionDir, processManager);
    }
}
