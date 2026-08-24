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
        Assert.Contains("--http-chunk-size", args);
        Assert.Contains("10M", args);
        Assert.Contains("15", args);
    }

    [Fact]
    public void DownloadCompativel_UsaMp4CombinadoComoUltimoRecurso()
    {
        var args = YouTubeAcquisition.CompatibleDownloadArguments([], "ffmpeg", "source.%(ext)s", "https://youtu.be/teste");
        Assert.Contains("18/b[height<=720][ext=mp4]/best[height<=720]", args);
        Assert.Contains("--http-chunk-size", args);
    }

    [Fact]
    public void Metadados_NaoBaixamVideo()
    {
        var args = YouTubeAcquisition.MetadataArguments([], "https://youtu.be/teste");
        Assert.Contains("--skip-download", args);
        Assert.Contains("duration", args);
        Assert.Contains("title", args);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Edge")]
    [InlineData("firefox")]
    public void SessaoDoNavegador_AdicionaCookiesComBrowserValidado(string browser)
    {
        var args = YouTubeAcquisition.WithBrowserSession(["--force-ipv4"], browser);
        Assert.Equal("--cookies-from-browser", args[^2]);
        Assert.Equal(browser.ToLowerInvariant(), args[^1]);
    }

    [Fact]
    public void SessaoDoNavegador_RejeitaValorArbitrario()
    {
        Assert.Throws<ArgumentException>(() => YouTubeAcquisition.WithBrowserSession([], "arquivo-injetado"));
    }

    [Fact]
    public void FalhaAntiRobo_RecebeCodigoParaInterfaceDeRecuperacao()
    {
        Assert.Equal("youtube-auth-required", ToolService.ClassifyFailure("Sign in to confirm you're not a bot"));
        Assert.Equal("youtube-cookie-access", ToolService.ClassifyFailure("Não foi possível acessar a sessão do navegador."));
    }
}
