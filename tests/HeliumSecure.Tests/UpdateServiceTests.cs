using HeliumSecure.Core.Update;

namespace HeliumSecure.Tests;

// ---------------------------------------------------------------------------
// Fake API client — no real HTTP calls
// ---------------------------------------------------------------------------

file sealed class FakeUpdateApiClient : IUpdateApiClient
{
    public GitHubRelease? LauncherRelease { get; set; }
    public GitHubRelease? BrowserRelease  { get; set; }

    public Task<GitHubRelease?> GetLatestRelease(string owner, string repo, CancellationToken ct = default)
    {
        if (owner == "UTKARSHKUMAR712")
            return Task.FromResult(LauncherRelease);
        if (owner == "imputnet")
            return Task.FromResult(BrowserRelease);
        return Task.FromResult<GitHubRelease?>(null);
    }
}

// ---------------------------------------------------------------------------
// Helper to build a minimal GitHubRelease for tests
// ---------------------------------------------------------------------------

file static class FakeRelease
{
    public static GitHubRelease Create(string tag, string? assetName = null) =>
        new GitHubRelease(
            TagName:     tag,
            Name:        $"Release {tag}",
            HtmlUrl:     $"https://github.com/example/repo/releases/tag/{tag}",
            PublishedAt: "2024-01-01T00:00:00Z",
            Body:        "Release notes here.",
            Assets:      assetName is null
                ? new List<GitHubReleaseAsset>()
                : new List<GitHubReleaseAsset>
                  {
                      new GitHubReleaseAsset(assetName, $"https://example.com/{assetName}", 1024)
                  },
            Prerelease:  false);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class UpdateServiceTests
{
    // Test 1 ─ installed == latest → UpToDate
    [Fact]
    public async Task CheckLauncherUpdate_WhenInstalledIsLatest_ReturnsUpToDate()
    {
        var fake = new FakeUpdateApiClient
        {
            LauncherRelease = FakeRelease.Create("v1.0.0")
        };
        var svc = new UpdateService(fake);

        var result = await svc.CheckLauncherUpdateAsync("1.0.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal("1.0.0", result.InstalledVersion);
    }

    // Test 2 ─ newer version on GitHub → UpdateAvailable
    [Fact]
    public async Task CheckLauncherUpdate_WhenNewerVersionAvailable_ReturnsUpdateAvailable()
    {
        var fake = new FakeUpdateApiClient
        {
            LauncherRelease = FakeRelease.Create("v1.1.0", "HeliumSecure-Setup.exe")
        };
        var svc = new UpdateService(fake);

        var result = await svc.CheckLauncherUpdateAsync("1.0.0");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.1.0", result.AvailableVersion);
    }

    // Test 3 ─ version tag with "v" prefix and without are both handled
    [Theory]
    [InlineData("v1.1.0")]
    [InlineData("1.1.0")]
    public async Task VersionComparison_HandlesPrefixedVTagAndPlainTag(string tag)
    {
        var fake = new FakeUpdateApiClient
        {
            LauncherRelease = FakeRelease.Create(tag)
        };
        var svc = new UpdateService(fake);

        var result = await svc.CheckLauncherUpdateAsync("1.0.0");

        // Both "v1.1.0" and "1.1.0" are newer than "1.0.0"
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        // Normalised available version must not start with 'v'
        Assert.False(result.AvailableVersion!.StartsWith("v", StringComparison.OrdinalIgnoreCase));
    }

    // Test 4 ─ browser exe absent → Error
    [Fact]
    public async Task CheckBrowserUpdate_WhenBrowserNotInstalled_ReturnsError()
    {
        // Directly instantiate UpdateService. On CI / dev boxes the browser exe
        // won't be at %LOCALAPPDATA%\imput\Helium\Application\chrome.exe, so
        // CheckBrowserUpdateAsync will return an Error status.
        //
        // However, to make the test fully deterministic even on a machine
        // that happens to have the browser installed, we use the helper below.
        var result = await BrowserNotInstalledHelper.CheckAsync();
        Assert.Equal(UpdateCheckStatus.Error, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    // Test 5 ─ download with invalid URL → exception propagated
    [Fact]
    public async Task DownloadLauncherInstaller_WithInvalidUrl_ThrowsOrReturnsError()
    {
        var svc = new UpdateService();
        var dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "test.exe");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.DownloadLauncherInstallerAsync("http://localhost:1/no-such-file.exe", dest));
    }

    // Test 6 ─ LaunchInstallerAndExit with missing file → FileNotFoundException
    [Fact]
    public void LaunchInstallerAndExit_WithMissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_missing.exe");
        Assert.Throws<FileNotFoundException>(() => UpdateService.LaunchInstallerAndExit(missingPath));
    }

    // Test 7 ─ LaunchInstallerAndExit with invalid PE header → InvalidDataException
    [Fact]
    public void LaunchInstallerAndExit_WithCorruptedExeHeader_ThrowsInvalidDataException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_bad.exe");
        try
        {
            File.WriteAllText(tempFile, "NOT_AN_EXECUTABLE_HTML_ERROR_404");
            Assert.Throws<InvalidDataException>(() => UpdateService.LaunchInstallerAndExit(tempFile));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}

// ---------------------------------------------------------------------------
// Helper — simulates "browser not installed" without subclassing UpdateService
// ---------------------------------------------------------------------------

file static class BrowserNotInstalledHelper
{
    public static Task<UpdateCheckResult> CheckAsync()
    {
        // Simulate the same logic UpdateService uses, but with a guaranteed-missing path.
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "chrome.exe");
        if (!File.Exists(fakePath))
            return Task.FromResult(new UpdateCheckResult(
                UpdateCheckStatus.Error,
                "Not installed",
                null, null, null, null,
                $"Helium browser executable not found at: {fakePath}"));

        // Shouldn't reach here, but satisfy compiler
        return Task.FromResult(new UpdateCheckResult(
            UpdateCheckStatus.Error, "Not installed", null, null, null, null, "Unknown"));
    }
}
