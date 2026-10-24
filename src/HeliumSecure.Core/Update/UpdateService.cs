using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeliumSecure.Core.Update;

// ---------------------------------------------------------------------------
// GitHub API models
// ---------------------------------------------------------------------------

public record GitHubRelease(
    [property: JsonPropertyName("tag_name")]   string TagName,
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("html_url")]   string HtmlUrl,
    [property: JsonPropertyName("published_at")] string PublishedAt,
    [property: JsonPropertyName("body")]       string? Body,
    [property: JsonPropertyName("assets")]     List<GitHubReleaseAsset> Assets,
    [property: JsonPropertyName("prerelease")] bool Prerelease);

public record GitHubReleaseAsset(
    [property: JsonPropertyName("name")]                 string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("size")]                 long Size);

// ---------------------------------------------------------------------------
// Update result types
// ---------------------------------------------------------------------------

public enum UpdateCheckStatus { Unknown, UpToDate, UpdateAvailable, Error }

public record UpdateCheckResult(
    UpdateCheckStatus Status,
    string InstalledVersion,
    string? AvailableVersion,
    string? ReleaseNotes,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? ErrorMessage);

// ---------------------------------------------------------------------------
// Abstraction for testability (HTTP layer)
// ---------------------------------------------------------------------------

public interface IUpdateApiClient
{
    Task<GitHubRelease?> GetLatestRelease(string owner, string repo, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Default HTTP implementation
// ---------------------------------------------------------------------------

internal sealed class HttpUpdateApiClient : IUpdateApiClient
{
    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HeliumSecure/1.0");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<GitHubRelease?> GetLatestRelease(string owner, string repo, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("GitHub API rate limit exceeded (HTTP 403).");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // No releases published yet

        response.EnsureSuccessStatusCode();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return await response.Content.ReadFromJsonAsync<GitHubRelease>(options, ct).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------------------
// UpdateService
// ---------------------------------------------------------------------------

public sealed class UpdateService
{
    private readonly IUpdateApiClient _api;

    /// <summary>Creates an UpdateService that uses the real GitHub API.</summary>
    public UpdateService() : this(new HttpUpdateApiClient()) { }

    /// <summary>Creates an UpdateService with a custom API client (for tests).</summary>
    public UpdateService(IUpdateApiClient api) => _api = api;

    // -----------------------------------------------------------------------
    // Launcher update check
    // -----------------------------------------------------------------------

    public async Task<UpdateCheckResult> CheckLauncherUpdateAsync(
        string installedVersion,
        CancellationToken ct = default)
    {
        try
        {
            var release = await _api.GetLatestRelease("UTKARSHKUMAR712", "Helium_secure", ct)
                                    .ConfigureAwait(false);

            if (release is null)
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    installedVersion,
                    null, null, null, null,
                    "No releases found on GitHub.");

            var tagVersion = NormaliseVersion(release.TagName);
            var status = CompareVersions(installedVersion, tagVersion);

            string? downloadUrl = null;
            if (status == UpdateCheckStatus.UpdateAvailable)
            {
                downloadUrl = release.Assets
                    .FirstOrDefault(a => a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ?.BrowserDownloadUrl
                    ?? release.Assets
                    .FirstOrDefault(a => a.Name.Contains("-win-x64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ?.BrowserDownloadUrl;
            }

            return new UpdateCheckResult(
                status,
                installedVersion,
                tagVersion,
                release.Body,
                release.HtmlUrl,
                downloadUrl,
                null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, installedVersion, null, null, null, null,
                "Update check was cancelled.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, installedVersion, null, null, null, null,
                $"Update check failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Browser update check
    // -----------------------------------------------------------------------

    public async Task<UpdateCheckResult> CheckBrowserUpdateAsync(CancellationToken ct = default)
    {
        // Resolve installed browser version
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var chromeExe = Path.Combine(localAppData, "imput", "Helium", "Application", "chrome.exe");

        string installedVersion;
        try
        {
            if (!File.Exists(chromeExe))
                return new UpdateCheckResult(UpdateCheckStatus.Error, "Not installed", null, null, null, null,
                    $"Helium browser executable not found at: {chromeExe}");

            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(chromeExe);
            installedVersion = fvi.FileVersion ?? "0.0.0.0";
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, "Unknown", null, null, null, null,
                $"Could not read browser version: {ex.Message}");
        }

        try
        {
            var release = await _api.GetLatestRelease("imputnet", "helium-windows", ct)
                                    .ConfigureAwait(false);

            if (release is null)
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    installedVersion,
                    null, null, null, null,
                    "No browser releases found on GitHub.");

            var tagVersion = NormaliseVersion(release.TagName);
            var status = CompareVersions(installedVersion, tagVersion);

            // Pick x64 full installer URL
            string? downloadUrl = release.Assets
                .FirstOrDefault(a => a.Name.EndsWith("x64-installer.exe", StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;

            return new UpdateCheckResult(
                status,
                installedVersion,
                tagVersion,
                release.Body,
                release.HtmlUrl,
                downloadUrl,
                null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, installedVersion, null, null, null, null,
                "Update check was cancelled.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, installedVersion, null, null, null, null,
                $"Browser update check failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Download launcher installer
    // -----------------------------------------------------------------------

    public async Task<string> DownloadLauncherInstallerAsync(
        string downloadUrl,
        string destPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HeliumSecure/1.0");

        using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                                       .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloadedBytes += read;

            if (progress != null && totalBytes > 0)
            {
                var pct = (int)Math.Clamp(downloadedBytes * 100 / totalBytes, 0, 100);
                progress.Report(pct);
            }
        }

        // Validate download completeness and integrity
        var fileInfo = new FileInfo(destPath);
        if (fileInfo.Length == 0)
            throw new InvalidOperationException("Downloaded installer file is empty.");

        if (totalBytes > 0 && downloadedBytes != totalBytes)
            throw new InvalidOperationException($"Incomplete download: received {downloadedBytes} bytes, expected {totalBytes} bytes.");

        // If installer is an executable, verify it possesses a valid PE header (MZ magic bytes)
        if (destPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            using var checkFs = new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (checkFs.Length < 64)
                throw new InvalidDataException("Downloaded file is too small to be a valid Windows executable.");
            var magic = new byte[2];
            if (checkFs.Read(magic, 0, 2) != 2 || magic[0] != 0x4D || magic[1] != 0x5A) // 'M', 'Z'
                throw new InvalidDataException("Downloaded file does not contain a valid Windows executable (MZ) header.");
        }

        progress?.Report(100);
        return destPath;
    }

    // -----------------------------------------------------------------------
    // Launch installer and exit
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies the installer exists, validates executable integrity, launches it, then invokes <paramref name="shutdownCallback"/>
    /// (pass <c>() => Application.Current.Shutdown()</c> from WPF layer).
    /// </summary>
    public static void LaunchInstallerAndExit(string installerPath, Action? shutdownCallback = null)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer file not found.", installerPath);

        // Security check: verify valid PE executable header before execution
        if (installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 64)
                throw new InvalidDataException("Installer file is too small to be a valid Windows executable.");
            var magic = new byte[2];
            if (fs.Read(magic, 0, 2) != 2 || magic[0] != 0x4D || magic[1] != 0x5A) // 'M', 'Z'
                throw new InvalidDataException("Installer file is corrupt or not a valid Windows executable.");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });
        shutdownCallback?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Strips leading 'v' from version tags like "v1.1.0".</summary>
    private static string NormaliseVersion(string tag)
    {
        var s = tag.Trim();
        return s.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? s[1..] : s;
    }

    private static UpdateCheckStatus CompareVersions(string installed, string available)
    {
        // Parse; fall back gracefully if either is non-standard
        if (!System.Version.TryParse(installed, out var iv))
            iv = new System.Version(0, 0, 0, 0);
        if (!System.Version.TryParse(available, out var av))
            av = new System.Version(0, 0, 0, 0);

        return av > iv ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
    }
}
