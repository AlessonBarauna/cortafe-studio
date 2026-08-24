using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class SilenceTrimmingServiceTests
{
    private readonly SilenceTrimmingService _service = new();

    [Fact]
    public void Plan_ReduzSomentePausasLongasSemTocarPalavras()
    {
        var clip = new ClipCandidate { Start = 0, End = 75, EditorialProfile = "pregacao" }; var transcript = TranscriptWithLongPauses();
        var plan = _service.Plan(clip, transcript); var words = transcript.SelectMany(segment => segment.Words).ToList();

        Assert.True(plan.Applied);
        Assert.True(plan.FinalDuration >= 60);
        Assert.All(plan.Cuts, cut => Assert.DoesNotContain(words, word => cut.Start < word.End && cut.End > word.Start));
        Assert.All(plan.Cuts, cut => Assert.True(cut.Duration <= 8));
    }

    [Fact]
    public void Plan_PreservaRespiracoesEPausasNaturais()
    {
        var transcript = Segment((0, .4, "uma"), (.9, 1.2, "pausa"), (1.8, 2.1, "natural"));
        var plan = _service.Plan(new ClipCandidate { Start = 0, End = 60, EditorialProfile = "podcast" }, transcript);
        Assert.False(plan.Applied);
        Assert.Equal(60, plan.FinalDuration);
    }

    [Fact]
    public void Plan_PreservaDinamicaDeLouvor()
    {
        var clip = new ClipCandidate { Start = 0, End = 75, EditorialProfile = "louvor" };
        var plan = _service.Plan(clip, TranscriptWithLongPauses());
        Assert.False(plan.Applied);
        Assert.Contains("musical", plan.Reason);
    }

    [Fact]
    public void Filters_GeramSelectSincronizadoParaAudioEVideo()
    {
        var plan = new SilenceTrimPlan { Cuts = [new SilenceCut { Start = 12, End = 14 }], OriginalDuration = 70, FinalDuration = 68, RemovedDuration = 2 };
        Assert.Equal("select='not(between(t\\,2\\,4))',setpts=N/FRAME_RATE/TB,", SilenceTrimmingService.VideoPrefix(plan, 10));
        Assert.Equal("aselect='not(between(t\\,2\\,4))',asetpts=N/SR/TB,", SilenceTrimmingService.AudioPrefix(plan, 10));
    }

    [Fact]
    public void AdjustWords_CompensaTempoRemovidoAntesDaLegenda()
    {
        var plan = new SilenceTrimPlan { Cuts = [new SilenceCut { Start = 5, End = 7 }], OriginalDuration = 65, FinalDuration = 63, RemovedDuration = 2 };
        var adjusted = SilenceTrimmingService.AdjustWords([new TranscriptWord { Start = 8, End = 8.5, Word = "sincronizada" }], plan);
        Assert.Equal(6, adjusted[0].Start);
        Assert.Equal(6.5, adjusted[0].End);
    }

    private static List<TranscriptSegment> TranscriptWithLongPauses() => Segment((.2, .6, "inicio"), (1, 1.4, "natural"), (4.5, 5, "retoma"), (6, 6.4, "fala"), (12, 12.4, "continua"), (72, 72.4, "conclusao"), (74, 74.4, "final"));
    private static List<TranscriptSegment> Segment(params (double Start, double End, string Text)[] values) => [new TranscriptSegment { Start = values[0].Start, End = values[^1].End, Text = string.Join(' ', values.Select(value => value.Text)), Words = values.Select(value => new TranscriptWord { Start = value.Start, End = value.End, Word = value.Text }).ToList() }];
}
