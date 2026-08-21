using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class TikTokContentPostingServiceTests
{
    [Fact]
    public void ChunkPlan_VideoPequeno_UsaArquivoInteiro()
    {
        var size = 4L * 1024 * 1024;
        var plan = TikTokContentPostingService.ChunkPlan(size);

        Assert.Equal(size, plan.ChunkSize);
        Assert.Equal(1, plan.TotalChunks);
    }

    [Fact]
    public void ChunkPlan_VideoGrande_UsaChunksPermitidos()
    {
        var size = 75L * 1024 * 1024;
        var plan = TikTokContentPostingService.ChunkPlan(size);

        Assert.InRange(plan.ChunkSize,
            TikTokContentPostingService.MinChunkSize,
            TikTokContentPostingService.MaxChunkSize);
        Assert.True(plan.TotalChunks > 1);
    }

    [Fact]
    public void ResolvePrivacy_PublicaPublico_QuandoPermitido()
    {
        var privacy = TikTokContentPostingService.ResolvePrivacy(
            "public",
            ["PUBLIC_TO_EVERYONE", "SELF_ONLY"]);

        Assert.Equal("PUBLIC_TO_EVERYONE", privacy);
    }

    [Fact]
    public void ResolvePrivacy_CaiParaPrivado_QuandoPublicoNaoDisponivel()
    {
        var privacy = TikTokContentPostingService.ResolvePrivacy(
            "public",
            ["SELF_ONLY"]);

        Assert.Equal("SELF_ONLY", privacy);
    }

    [Fact]
    public void ResolvePrivacy_RecusaQuandoNaoHaOpcoes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TikTokContentPostingService.ResolvePrivacy("private", []));

        Assert.Contains("opções de privacidade", error.Message);
    }
}
