using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BezelFreeCorrection.Updates;

// Lightweight GitHub-releases poller. The app is framework-dependent
// and distributed via a standalone installer, so updating == running
// a fresh installer: this class just finds the newest release, decides
// whether it's worth offering, and spawns the installer the user
// downloads. No in-process replacement, no service, no daemon.
public static class UpdateChecker
{
    public sealed record ReleaseInfo(
        string Version,
        string HtmlUrl,
        string? InstallerUrl,
        string? InstallerFileName);

    // Polls GitHub for the latest non-draft, non-prerelease release and
    // compares it against the running assembly's version. Returns null
    // if there is no newer release, if the network fails, or if the
    // current build is already ahead. Designed to be fire-and-forget.
    public static async Task<ReleaseInfo?> CheckAsync(
        string owner, string repo, string currentVersion,
        CancellationToken cancel = default)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("WallpaperBezelFreeCorrection", currentVersion));
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            var json = await http.GetStringAsync(url, cancel).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GhRelease>(json, JsonOpts);
            if (release == null) return null;
            if (release.Draft || release.Prerelease) return null;

            var tag = release.TagName?.TrimStart('v') ?? string.Empty;
            if (string.IsNullOrEmpty(tag)) return null;
            if (!IsNewer(tag, currentVersion)) return null;

            // Prefer a .exe asset (Inno Setup output). Fall back to the
            // first asset if the installer is named differently on some
            // release — the update prompt still works, the user just
            // lands on the generic download.
            var exeAsset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
            var anyAsset = exeAsset ?? release.Assets?.FirstOrDefault();
            return new ReleaseInfo(
                Version: tag,
                HtmlUrl: release.HtmlUrl ?? $"https://github.com/{owner}/{repo}/releases/latest",
                InstallerUrl: anyAsset?.BrowserDownloadUrl,
                InstallerFileName: anyAsset?.Name);
        }
        catch
        {
            // Network failure, rate-limit, parsing issue — all treated
            // the same: silently give up, the app continues to work.
            return null;
        }
    }

    // Downloads the installer to %TEMP% and launches it. Returns the
    // spawned process so the host can exit to let the installer
    // replace the current build.
    public static async Task<Process?> DownloadAndLaunchInstallerAsync(
        ReleaseInfo info, CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(info.InstallerUrl)) return null;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("WallpaperBezelFreeCorrection", info.Version));

        var fileName = string.IsNullOrEmpty(info.InstallerFileName)
            ? $"WallpaperBezelFreeCorrection-{info.Version}.exe"
            : info.InstallerFileName;
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        using (var src = await http.GetStreamAsync(info.InstallerUrl, cancel).ConfigureAwait(false))
        using (var dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst, cancel).ConfigureAwait(false);
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true,
        });
    }

    // Lexicographic semver comparison good enough for x.y.z tags.
    private static bool IsNewer(string candidate, string current)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var ai = i < a.Length ? a[i] : 0;
            var bi = i < b.Length ? b[i] : 0;
            if (ai > bi) return true;
            if (ai < bi) return false;
        }
        return false;
    }

    private static int[] ParseVersion(string v)
    {
        return v.Split('.')
            .Select(p =>
            {
                var digits = new string(p.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out var n) ? n : 0;
            })
            .ToArray();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")]     public string? TagName { get; set; }
        [JsonPropertyName("html_url")]     public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")]        public bool Draft { get; set; }
        [JsonPropertyName("prerelease")]   public bool Prerelease { get; set; }
        [JsonPropertyName("assets")]       public GhAsset[]? Assets { get; set; }
    }

    private sealed class GhAsset
    {
        [JsonPropertyName("name")]                 public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
