namespace CortaFeStudio.Api.Services;

public static class YouTubeAcquisition
{
    public const string FastRenderFormat = "bv*[vcodec^=avc1][height<=1080]+ba/b[vcodec^=avc1][height<=1080]/bv*[height<=1080]+ba/b[height<=1080]";

    public static List<string> MetadataArguments(IEnumerable<string> common, string url) =>
        [.. common, "--no-playlist", "--skip-download", "--print", "duration", url];

    public static List<string> DownloadArguments(IEnumerable<string> common, string ffmpeg, string output, string url) =>
        [.. common, "--no-playlist", "--concurrent-fragments", "4", "--ffmpeg-location", ffmpeg, "--merge-output-format", "mp4", "-f", FastRenderFormat, "-o", output, "--print", "after_move:filepath", url];
}
