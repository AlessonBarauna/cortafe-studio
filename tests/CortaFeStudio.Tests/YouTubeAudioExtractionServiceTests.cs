using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class YouTubeAudioExtractionServiceTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", true)]
    [InlineData("https://youtu.be/abc", true)]
    [InlineData("https://music.youtube.com/watch?v=abc", true)]
    [InlineData("https://example.com/watch?v=abc", false)]
    [InlineData("https://twitch.tv/canal", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsYouTubeUrl_ValidaSomenteHostsDoYouTube(string url, bool expected)
        => Assert.Equal(expected, YouTubeAudioExtractionService.IsYouTubeUrl(url));

    [Fact]
    public void BuildArguments_Mp3AltaQualidade_NaoBaixaPlaylistPorPadrao()
    {
        var args = YouTubeAudioExtractionService.BuildArguments(
            ["--force-ipv4"],
            "ffmpeg",
            Path.Combine("C:", "Audios"),
            "https://www.youtube.com/watch?v=abc",
            "mp3",
            "high",
            playlist: false,
            organizeByChannel: true,
            organizeByPlaylist: true);

        Assert.Contains("--no-playlist", args);
        Assert.Contains("--audio-format", args);
        Assert.Contains("mp3", args);
        Assert.Contains("--audio-quality", args);
        Assert.Contains("0", args);
        Assert.DoesNotContain("--playlist-end", args);
        Assert.Contains(args, value => value.Contains("%(uploader,channel|Canal)s"));
        Assert.Equal("https://www.youtube.com/watch?v=abc", args[^1]);
    }

    [Fact]
    public void BuildArguments_Playlist_LimitaDuzentosItensEOrganizaPastas()
    {
        var args = YouTubeAudioExtractionService.BuildArguments(
            [], "ffmpeg", Path.GetTempPath(),
            "https://www.youtube.com/playlist?list=abc",
            "m4a", "compact",
            playlist: true,
            organizeByChannel: true,
            organizeByPlaylist: true);

        Assert.Contains("--yes-playlist", args);
        var limitIndex = args.IndexOf("--playlist-end");
        Assert.True(limitIndex >= 0);
        Assert.Equal("200", args[limitIndex + 1]);
        Assert.Contains(args, value => value.Contains("%(playlist_title|Playlist)s"));
        Assert.Contains(args, value => value.Contains("%(playlist_index|0)03d"));
        Assert.Contains("128K", args);
    }

    [Fact]
    public void NormalizeFormatEQuality_UsamFallbackSeguro()
    {
        Assert.Equal("mp3", YouTubeAudioExtractionService.NormalizeFormat("exe"));
        Assert.Equal("high", YouTubeAudioExtractionService.NormalizeQuality("ultra"));
        Assert.Equal("wav", YouTubeAudioExtractionService.NormalizeFormat("WAV"));
        Assert.Equal("compact", YouTubeAudioExtractionService.NormalizeQuality("COMPACT"));
    }
}
