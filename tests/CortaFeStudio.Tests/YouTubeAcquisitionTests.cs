using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class YouTubeAcquisitionTests
{
    [Fact]
    public void Download_PrefereH264EFragmentosConcorrentes()
    {
        var args = YouTubeAcquisition.DownloadArguments(["--force-ipv4"], "ffmpeg", "source.%(ext)s", "https://youtu.be/teste");
        Assert.Contains("--concurrent-fragments", args);
        Assert.Contains("4", args);
        Assert.Contains("vcodec^=avc1", string.Join(' ', args));
    }

    [Fact]
    public void Metadados_NaoBaixamVideo()
    {
        var args = YouTubeAcquisition.MetadataArguments([], "https://youtu.be/teste");
        Assert.Contains("--skip-download", args);
        Assert.Contains("duration", args);
        Assert.Contains("title", args);
    }
}
