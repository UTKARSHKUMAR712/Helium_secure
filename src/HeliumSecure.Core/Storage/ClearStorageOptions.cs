namespace HeliumSecure.Core.Storage;

public sealed record ClearStorageOptions(
    bool ClearCache = true,
    bool ClearHistory = false,
    bool ClearCookiesAndSiteData = false,
    bool ClearDownloads = false
);
