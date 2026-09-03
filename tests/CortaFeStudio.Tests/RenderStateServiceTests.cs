using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RenderStateServiceTests
{
    [Fact]
    public void Fingerprint_MudaQuandoConfiguracaoVisualMuda()
    {
        var clip = new ClipCandidate { Start = 0, End = 60, Title = "Original", VideoPath = "clip.mp4" };
        var before = RenderStateService.Fingerprint(clip);
        clip.Title = "Título editado";
        RenderStateService.MarkIfChanged(clip, before);
        Assert.True(clip.RenderOutdated);
        Assert.NotEqual(before, RenderStateService.Fingerprint(clip));
    }

    [Fact]
    public void Fingerprint_IncluiTextoEditadoDaLegenda()
    {
        var clip = new ClipCandidate { SubtitleTrack = new SubtitleTrack { Blocks = [new SubtitleBlock { Start = 0, End = 2, Text = "Texto original" }] } };
        var before = RenderStateService.Fingerprint(clip);
        clip.SubtitleTrack.Blocks[0].Text = "Texto corrigido";
        Assert.NotEqual(before, RenderStateService.Fingerprint(clip));
    }

    [Fact]
    public void Fingerprint_CobreOpcoesQueAlteramPreviewERender()
    {
        var original = Sample();
        AssertChanges(original, clip => clip.CropX = .72);
        AssertChanges(original, clip => clip.CropFocus = "top");
        AssertChanges(original, clip => clip.LayoutMode = "blur");
        AssertChanges(original, clip => clip.SplitLeftX = .18);
        AssertChanges(original, clip => clip.SplitRightX = .82);
        AssertChanges(original, clip => clip.OutputPreset = "square");
        AssertChanges(original, clip => clip.TransitionStyle = "dynamic");
        AssertChanges(original, clip => clip.PlaybackSpeed = 1.25);
        AssertChanges(original, clip => clip.SilenceTrimmingEnabled = false);
        AssertChanges(original, clip => clip.SubtitleStyle = "sermon");
        AssertChanges(original, clip => clip.BrandFrameEnabled = false);
        AssertChanges(original, clip => clip.BrandTheme = "podcast");
        AssertChanges(original, clip => clip.WatermarkEnabled = false);
        AssertChanges(original, clip => clip.WatermarkText = "MINHA MARCA");
        AssertChanges(original, clip => clip.WatermarkOpacity = .4);
        AssertChanges(original, clip => clip.FramingTrack = [new FramingKeyframe { Time = 1.2, X = .3 }, new FramingKeyframe { Time = 3.8, X = .7 }]);
    }

    private static void AssertChanges(ClipCandidate original, Action<ClipCandidate> change)
    {
        var clip = Clone(original);
        var before = RenderStateService.Fingerprint(clip);
        change(clip);
        Assert.NotEqual(before, RenderStateService.Fingerprint(clip));
    }

    private static ClipCandidate Sample() => new()
    {
        Start = 10,
        End = 72,
        Title = "Teste",
        CropFocus = "center",
        CropX = .5,
        LayoutMode = "fill",
        SplitLeftX = .25,
        SplitRightX = .75,
        OutputPreset = "vertical",
        TransitionStyle = "smooth",
        SubtitleStyle = "impact",
        SubtitleTrack = new SubtitleTrack { Enabled = true, Style = "impact", LayoutVersion = 2 },
        BrandFrameEnabled = true,
        BrandTheme = "amado-jesus",
        WatermarkEnabled = true,
        WatermarkText = "AJ  |  AMADO JESUS",
        WatermarkOpacity = .82,
        PlaybackSpeed = 1,
        SilenceTrimmingEnabled = true
    };

    private static ClipCandidate Clone(ClipCandidate source) => new()
    {
        Start = source.Start,
        End = source.End,
        Title = source.Title,
        CropFocus = source.CropFocus,
        CropX = source.CropX,
        FramingTrack = source.FramingTrack.Select(point => new FramingKeyframe { Time = point.Time, X = point.X }).ToList(),
        LayoutMode = source.LayoutMode,
        SplitLeftX = source.SplitLeftX,
        SplitRightX = source.SplitRightX,
        OutputPreset = source.OutputPreset,
        TransitionStyle = source.TransitionStyle,
        SubtitleStyle = source.SubtitleStyle,
        SubtitleTrack = source.SubtitleTrack is null ? null : new SubtitleTrack { Enabled = source.SubtitleTrack.Enabled, Style = source.SubtitleTrack.Style, LayoutVersion = source.SubtitleTrack.LayoutVersion },
        BrandFrameEnabled = source.BrandFrameEnabled,
        BrandTheme = source.BrandTheme,
        WatermarkEnabled = source.WatermarkEnabled,
        WatermarkText = source.WatermarkText,
        WatermarkOpacity = source.WatermarkOpacity,
        PlaybackSpeed = source.PlaybackSpeed,
        SilenceTrimmingEnabled = source.SilenceTrimmingEnabled
    };
}
