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

            var asset = release.assets?.FirstOrDefault(a =>
                string.Equals(a.name, cfg.InstallerAssetName, StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                log.LogWarning("Update check: installer asset not found: {asset}", cfg.InstallerAssetName);
                return null;
            }

            return new UpdateInfo(tagVersion, asset.name, asset.browser_download_url);
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
