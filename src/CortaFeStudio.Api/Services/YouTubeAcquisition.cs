namespace CortaFeStudio.Api.Services;

public static class YouTubeAcquisition
{
    private static readonly HashSet<string> SupportedBrowsers = new(StringComparer.OrdinalIgnoreCase)
        { "brave", "chrome", "chromium", "edge", "firefox", "opera", "vivaldi", "whale" };
    public const string FastRenderFormat = "bv*[vcodec^=avc1][height<=1080]+ba/b[vcodec^=avc1][height<=1080]/bv*[height<=1080]+ba/b[height<=1080]";

    public static List<string> MetadataArguments(IEnumerable<string> common, string url) =>
        [.. common, "--no-playlist", "--skip-download", "--print", "duration", "--print", "title", url];

    public static List<string> WithBrowserSession(IEnumerable<string> common, string? browser)
    {
        var args = common.ToList();
        if (string.IsNullOrWhiteSpace(browser)) return args;
        var normalized = browser.Trim().ToLowerInvariant();
        if (!SupportedBrowsers.Contains(normalized))
            throw new ArgumentException("Navegador não compatível. Use Chrome, Edge, Firefox, Brave, Chromium, Opera ou Vivaldi.", nameof(browser));
        args.AddRange(["--cookies-from-browser", normalized]);
        return args;
    }

    public static List<string> DownloadArguments(IEnumerable<string> common, string ffmpeg, string output, string url) =>
        [.. common, .. ResilienceArguments(), "--no-playlist", "--concurrent-fragments", "4", "--ffmpeg-location", ffmpeg, "--merge-output-format", "mp4", "-f", FastRenderFormat, "-o", output, "--print", "after_move:filepath", url];

    public static List<string> CompatibleDownloadArguments(IEnumerable<string> common, string ffmpeg, string output, string url) =>
        [.. common, .. ResilienceArguments(), "--no-playlist", "--ffmpeg-location", ffmpeg, "--merge-output-format", "mp4", "-f", "18/b[height<=720][ext=mp4]/best[height<=720]", "-o", output, "--print", "after_move:filepath", url];

    private static string[] ResilienceArguments() => ["--retries", "15", "--fragment-retries", "15", "--extractor-retries", "5", "--retry-sleep", "linear=1::3", "--http-chunk-size", "10M"];
}
