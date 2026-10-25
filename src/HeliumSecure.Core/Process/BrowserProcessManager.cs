using System.Diagnostics;
using System.IO;

namespace HeliumSecure.Core.Process;

public sealed class BrowserProcessManager : IDisposable
{
    private JobObject? _jobObject;
    private System.Diagnostics.Process? _rootProcess;
    private bool _disposed;

    public string? ExecutablePath { get; }

    public BrowserProcessManager(string? customExecutablePath = null)
    {
        ExecutablePath = ResolveHeliumExecutable(customExecutablePath);
    }

    /// <summary>
    /// Resolves the Helium browser executable from bundled path, local AppData, or environment.
    /// </summary>
    public static string? ResolveHeliumExecutable(string? candidatePath = null)
    {
        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
            // Explicit candidate was requested but does not exist
            return null;
        }

        // 1. Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("HELIUM_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // 2. Relative to current launcher directory (for standalone portable/installer distribution)
        string baseDir = AppContext.BaseDirectory;
        string bundledCandidate1 = Path.Combine(baseDir, "browser", "chrome.exe");
        if (File.Exists(bundledCandidate1)) return bundledCandidate1;

        string bundledCandidate2 = Path.Combine(baseDir, "chrome.exe");
        if (File.Exists(bundledCandidate2)) return bundledCandidate2;

        // 3. Upstream installed Helium in AppData
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string installedPath = Path.Combine(localAppData, "imput", "Helium", "Application", "chrome.exe");
        if (File.Exists(installedPath))
        {
            return installedPath;
        }

        return null;
    }

    /// <summary>
    /// Launches Helium with the isolated user-data-dir and attaches it to the tracking Job Object.
    /// Strict Chromium flags are passed to prevent background processes from persisting when the window is closed.
    /// </summary>
    public System.Diagnostics.Process Launch(string userDataDir, string? initialUrl = null, string? additionalArgs = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(ExecutablePath) || !File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException($"Helium executable could not be found at '{ExecutablePath}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDir);
        Directory.CreateDirectory(userDataDir);

        _jobObject?.Dispose();
        _jobObject = new JobObject();

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // Strict argument sanitization and formatting:
        // Ensure background mode, background sync, and component updaters are disabled so the browser
        // completely closes when the user clicks the 'X' (cross) button.
        var arguments = new List<string>
        {
            $"--user-data-dir=\"{userDataDir}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-mode",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-sync",
            "--disable-breakpad",
            "--disable-default-apps",
            "--no-pings"
        };

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            arguments.Add(additionalArgs);
        }

        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            arguments.Add($"\"{initialUrl}\"");
        }

        startInfo.Arguments = string.Join(" ", arguments);

        _rootProcess = System.Diagnostics.Process.Start(startInfo);
        if (_rootProcess == null)
        {
            throw new InvalidOperationException("Failed to start browser process.");
        }

        // Assign to Job Object for tracking and enforcing termination of all child processes
        _jobObject.AssignProcess(_rootProcess);

        return _rootProcess;
    }

    /// <summary>
    /// Asynchronously monitors and waits until the browser process and all child processes exit.
    /// Once the main window exits, gives a brief grace period to flush disk buffers, then
    /// terminates all child background processes immediately to prevent lingering tasks.
    /// </summary>
    public async Task WaitForExitAsync(int pollIntervalMs = 250, int maxGracePeriodMs = 2000, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_rootProcess == null)
        {
            return;
        }

        try
        {
            // First wait for the main window process to exit
            await _rootProcess.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Terminate();
            throw;
        }

        // Once the main window exits, wait briefly for renderers/GPU to exit cleanly
        var stopwatch = Stopwatch.StartNew();
        while (_jobObject != null && _jobObject.GetActiveProcessCount() > 0)
        {
            if (stopwatch.ElapsedMilliseconds > maxGracePeriodMs)
            {
                break; // Grace period expired - terminate lingering processes
            }

            await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
        }

        // Forcibly terminate any lingering background processes in the Job Object
        _jobObject?.Terminate(0);
    }

    /// <summary>
    /// Forcibly terminates the browser process and all child processes in the Job Object.
    /// </summary>
    public void Terminate()
    {
        try
        {
            _jobObject?.Terminate(0);
        }
        catch { }

        try
        {
            if (_rootProcess != null && !_rootProcess.HasExited)
            {
                _rootProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Terminate();
            _rootProcess?.Dispose();
            _jobObject?.Dispose();
            _disposed = true;
        }
    }
}
