using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RenderFilterFactoryTests
{
    [Theory]
    [InlineData(-1, "iw*0-540")]
    [InlineData(.5, "iw*0.5-540")]
    [InlineData(2, "iw*1-540")]
    public void CropX_LimitaFocoEEscapaVirgulas(double focus, string expected)
    {
        var expression = RenderFilterFactory.CropX(focus);
        Assert.Contains(expected, expression);
        Assert.Contains("0\\,min(iw-1080\\,", expression);
        Assert.DoesNotContain("0,min(iw-1080,", expression);
    }

    [Theory]
    [InlineData("top", "0")]
    [InlineData("center", "(ih-1920)/2")]
    [InlineData("bottom", "ih-1920")]
    public void CropY_RespeitaFocoVertical(string focus, string expected) =>
        Assert.Equal(expected, RenderFilterFactory.CropY(focus));

    [Fact]
    public void Framing_ModoDesfocado_MontaComposicaoCompleta()
    {
        var filter = RenderFilterFactory.Framing(new ClipCandidate { LayoutMode = "blur" });
        Assert.Contains("gblur=sigma=34", filter);
        Assert.Contains("overlay=(W-w)/2:(H-h)/2", filter);
    }
}
