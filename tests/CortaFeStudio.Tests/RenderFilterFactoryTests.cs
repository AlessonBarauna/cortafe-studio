using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RenderFilterFactoryTests
{
    [Fact]
    public void IdentidadeVisual_IncluiMolduraTemaEMarcaDagua()
    {
        var clip = new ClipCandidate { BrandTheme = "worship", BrandFrameEnabled = true, WatermarkEnabled = true, WatermarkText = "AMADO JESUS" };
        var filter = RenderFilterFactory.Branding(clip, "watermark.txt", ":font='Arial'");

        Assert.Contains("0xB98CFF", filter);
        Assert.Contains("drawbox", filter);
        Assert.Contains("drawtext", filter);
        Assert.Contains("watermark.txt", filter);
        Assert.Contains("x=(w-text_w)/2", filter);
        Assert.Contains("y=h-text_h-42", filter);
        Assert.Equal(3, filter.Split("drawtext").Length - 1);
    }
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

    [Fact]
    public void CreativeLook_AplicaAcabamentoOldSchoolSemExagero()
    {
        var filter = RenderFilterFactory.CreativeLook(60);

        Assert.Contains("colorbalance", filter);
        Assert.Contains("saturation=0.93", filter);
        Assert.Contains("vignette=PI/8", filter);
        Assert.Contains("noise=alls=1.2", filter);
        Assert.Contains("fade=t=out:st=59.72", filter);
    }

    [Fact]
    public void Framing_SemGatilhoIncluiMovimentoDeAssinatura()
    {
        var filter = RenderFilterFactory.Framing(new ClipCandidate { Transcript = "trecho sem palavra de impacto suficiente para o movimento automático" });
        Assert.Contains("between(t\\,0.2\\,2.8)", filter);
        Assert.Contains("0.026*sin", filter);
    }

    [Fact]
    public void Audio_AplicaTratamentoProfissionalComCompressaoELimiter()
    {
        var filter = RenderFilterFactory.Audio(60);

        Assert.Contains("highpass=f=70", filter);
        Assert.Contains("lowpass=f=15000", filter);
        Assert.Contains("afftdn=nf=-28", filter);
        Assert.Contains("acompressor=threshold=0.125:ratio=2.5", filter);
        Assert.Contains("loudnorm=I=-16:LRA=9:TP=-1.5", filter);
        Assert.Contains("alimiter=limit=0.95", filter);
        Assert.Contains("level=disabled", filter);
        Assert.Contains("afade=t=in", filter);
        Assert.Contains("afade=t=out:st=59.82", filter);

        Assert.True(filter.IndexOf("acompressor", StringComparison.Ordinal) < filter.IndexOf("loudnorm", StringComparison.Ordinal));
        Assert.True(filter.IndexOf("loudnorm", StringComparison.Ordinal) < filter.IndexOf("alimiter", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("vertical", 1080, 1920)]
    [InlineData("portrait", 1080, 1350)]
    [InlineData("square", 1080, 1080)]
    [InlineData("landscape", 1920, 1080)]
    public void Dimensions_ResolvePresetProfissional(string preset, int width, int height) =>
        Assert.Equal((width, height), RenderFilterFactory.Dimensions(preset));

    [Fact]
    public void Framing_Horizontal_UsaResolucaoCorreta()
    {
        var filter = RenderFilterFactory.Framing(new ClipCandidate { OutputPreset = "landscape" });
        Assert.Contains("scale=1920:1080", filter);
        Assert.Contains("crop=1920:1080", filter);
    }

    [Fact]
    public void Framing_ComRastreamento_InterpolaMovimentoAoLongoDoTempo()
    {
        var clip = new ClipCandidate
        {
            FramingTrack =
            [
                new FramingKeyframe { Time = 0, X = .3 },
                new FramingKeyframe { Time = 2, X = .7 },
                new FramingKeyframe { Time = 4, X = .5 }
            ]
        };

        var filter = RenderFilterFactory.Framing(clip);

        Assert.Contains("if(lte(t\\,2)\\,0.3+(0.7-0.3)*(t-0)/2", filter);
        Assert.Contains("lte(t\\,4)", filter);
        Assert.Contains("*(t-2)/2", filter);
        Assert.Contains("iw*(", filter);
    }
}
