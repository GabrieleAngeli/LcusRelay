using System.Net.Http.Headers;
using System.Text.Json;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Services;

public sealed class GitHubUpdateService
{
    private static readonly HttpClient _http = CreateHttpClient();

    public async Task<UpdateInfo?> CheckForUpdateAsync(UpdateConfig cfg, Version currentVersion, ILogger log, CancellationToken ct)
    {
        if (!cfg.Enabled || !cfg.CheckOnStartup)
            return null;

        if (string.IsNullOrWhiteSpace(cfg.RepoOwner) || string.IsNullOrWhiteSpace(cfg.RepoName))
        {
            log.LogInformation("Update check skipped: RepoOwner/RepoName not configured.");
            return null;
        }

        var url = $"https://api.github.com/repos/{cfg.RepoOwner.Trim()}/{cfg.RepoName.Trim()}/releases/latest";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Update check failed: {status} {reason}", (int)resp.StatusCode, resp.ReasonPhrase);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release is null || string.IsNullOrWhiteSpace(release.tag_name))
                return null;

            var tagVersion = ParseVersion(release.tag_name);
            if (tagVersion is null)
            {
                log.LogWarning("Update check: invalid tag version {tag}", release.tag_name);
                return null;
            }

            if (tagVersion <= currentVersion)
                return null;

            var asset = ResolveInstallerAsset(release.assets, cfg);

            if (asset is null)
            {
                var availableAssets = release.assets is null || release.assets.Count == 0
                    ? "(none)"
                    : string.Join(", ", release.assets
                        .Select(a => a.name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!.Trim()));

                log.LogWarning(
                    "Update check: installer asset not found. Expected={asset}. Available={available}",
                    cfg.InstallerAssetName,
                    availableAssets);
                return null;
            }

            var assetName = asset.name?.Trim();
            var downloadUrl = asset.browser_download_url?.Trim();
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                log.LogWarning("Update check: selected asset is missing required metadata.");
                return null;
            }

            return new UpdateInfo(tagVersion, assetName, downloadUrl);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Update check failed.");
            return null;
        }
    }

    public async Task<string?> DownloadInstallerAsync(UpdateInfo info, ILogger log, CancellationToken ct)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), info.AssetName);
            using var resp = await _http.GetAsync(info.DownloadUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Installer download failed: {status} {reason}", (int)resp.StatusCode, resp.ReasonPhrase);
                return null;
            }

            await using var fs = File.Create(tempPath);
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            return tempPath;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Installer download failed.");
            return null;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var t = tag.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            t = t[1..];

        return Version.TryParse(t, out var v) ? v : null;
    }

    private static GitHubAsset? ResolveInstallerAsset(List<GitHubAsset>? assets, UpdateConfig cfg)
    {
        if (assets is null || assets.Count == 0)
            return null;

        var configuredName = (cfg.InstallerAssetName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            var exact = assets.FirstOrDefault(a =>
                string.Equals(a.name?.Trim(), configuredName, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
                return exact;
        }

        var repoName = (cfg.RepoName ?? "").Trim();

        var installerExe = assets.FirstOrDefault(a =>
        {
            var name = a.name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return false;

            return name.Contains("setup", StringComparison.OrdinalIgnoreCase)
                || name.Contains("installer", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(repoName) && name.Contains(repoName, StringComparison.OrdinalIgnoreCase));
        });

        if (installerExe is not null)
            return installerExe;

        return assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.name)
            && a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LcusRelay", "1.0"));
        return http;
    }

    private sealed class GitHubRelease
    {
        public string? tag_name { get; set; }
        public List<GitHubAsset>? assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? name { get; set; }
        public string? browser_download_url { get; set; }
    }
}

public sealed record UpdateInfo(Version Version, string AssetName, string DownloadUrl);
