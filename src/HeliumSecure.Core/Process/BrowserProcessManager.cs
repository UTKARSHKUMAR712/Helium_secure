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
            return null;
        }

        // 1. Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("HELIUM_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // 2. Resolve candidates (bundled or installed across standard paths) and select the highest version available
        string baseDir = AppContext.BaseDirectory;
        string bundledCandidate1 = Path.Combine(baseDir, "browser", "chrome.exe");
        string bundledCandidate2 = Path.Combine(baseDir, "chrome.exe");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string heliumSecureInstalledPath = Path.Combine(localAppData, "Programs", "HeliumSecure", "browser", "chrome.exe");
        string imputHeliumInstalledPath = Path.Combine(localAppData, "imput", "Helium", "Application", "chrome.exe");

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pfCandidate = Path.Combine(programFiles, "Helium", "Application", "chrome.exe");
        string pfX86Candidate = Path.Combine(programFilesX86, "Helium", "Application", "chrome.exe");

        var candidates = new[] {
            bundledCandidate1,
            bundledCandidate2,
            heliumSecureInstalledPath,
            imputHeliumInstalledPath,
            pfCandidate,
            pfX86Candidate
        }
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // If multiple installations exist, choose the one with the highest FileVersion
        string best = candidates[0];
        Version bestVer = ParseFileVersion(best);

        for (int i = 1; i < candidates.Count; i++)
        {
            var curVer = ParseFileVersion(candidates[i]);
            if (curVer > bestVer)
            {
                best = candidates[i];
                bestVer = curVer;
            }
        }

        return best;
    }

    public static Version ParseFileVersion(string path)
    {
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var verStr = fvi.FileVersion?.Trim();
            if (!string.IsNullOrEmpty(verStr))
            {
                if (verStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    verStr = verStr[1..];

                if (System.Version.TryParse(verStr, out var v))
                    return v;

                var clean = new string(verStr.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
                if (System.Version.TryParse(clean, out var cv))
                    return cv;
            }

            var prodStr = fvi.ProductVersion?.Trim();
            if (!string.IsNullOrEmpty(prodStr))
            {
                if (prodStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    prodStr = prodStr[1..];
                if (System.Version.TryParse(prodStr, out var pv))
                    return pv;
            }
        }
        catch { }
        return new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Ensures that the browser executable has a matching Windows Side-by-Side (SxS) manifest
    /// corresponding to its exact file/product version. If missing or mismatched, generates the valid manifest
    /// and removes legacy mismatched manifests to prevent SxS assembly load failures.
    /// </summary>
    public static void EnsureValidSideBySideManifest(string executablePath)
    {
        try
        {
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                return;

            var dir = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;

            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
            var verStr = fvi.FileVersion?.Trim();
            if (string.IsNullOrEmpty(verStr))
                verStr = fvi.ProductVersion?.Trim();

            if (string.IsNullOrEmpty(verStr))
                return;

            if (verStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                verStr = verStr[1..];

            var cleanVer = new string(verStr.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (string.IsNullOrEmpty(cleanVer))
                cleanVer = verStr;

            var targetManifest = Path.Combine(dir, $"{cleanVer}.manifest");

            if (!File.Exists(targetManifest) || new FileInfo(targetManifest).Length == 0)
            {
                var content = $"<assembly\n  xmlns='urn:schemas-microsoft-com:asm.v1' manifestVersion='1.0'>\n  <assemblyIdentity\n      name='{cleanVer}'\n      version='{cleanVer}'\n      type='win32'/>\n  <file name='chrome_elf.dll'/>\n</assembly>";
                File.WriteAllText(targetManifest, content, System.Text.Encoding.UTF8);
            }

            // Remove any obsolete .manifest files in the same directory that do not match the binary version
            foreach (var mf in Directory.GetFiles(dir, "*.manifest", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(mf);
                if (!string.Equals(fileName, $"{cleanVer}.manifest", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(mf); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Launches Helium with the isolated user-data-dir and attaches it to the tracking Job Object.
    /// Helium browser runs with independent lifecycle and will not be killed if the launcher closes.
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

        // Ensure Windows Side-by-Side (SxS) manifest matches executable before starting
        EnsureValidSideBySideManifest(ExecutablePath);

        _jobObject?.Dispose();
        _jobObject = new JobObject();

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ?? "",
            UseShellExecute = false,
            CreateNoWindow = false
        };

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

        _jobObject.AssignProcess(_rootProcess);

        return _rootProcess;
    }

    /// <summary>
    /// Asynchronously monitors and waits until the browser process and all child processes exit.
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
            await _rootProcess.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        var stopwatch = Stopwatch.StartNew();
        while (_jobObject != null && _jobObject.GetActiveProcessCount() > 0)
        {
            if (stopwatch.ElapsedMilliseconds > maxGracePeriodMs)
            {
                break;
            }

            await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Explicitly terminates the browser process and all child processes when requested.
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

    /// <summary>
    /// Disposes resources. Closing the launcher does NOT kill the Helium browser.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _rootProcess?.Dispose();
            _jobObject?.Dispose();
            _disposed = true;
        }
    }
}
