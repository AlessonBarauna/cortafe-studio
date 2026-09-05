using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class TranscriptEditingTests
{
    private readonly SilenceTrimmingService _service = new();

    [Fact]
    public void Plan_AplicaCorteManualMesmoComReducaoDePausasDesligada()
    {
        var clip = new ClipCandidate
        {
            Start = 100,
            End = 170,
            SilenceTrimmingEnabled = false,
            SubtitleTrack = new SubtitleTrack
            {
                VideoCuts = [new TranscriptCut { Start = 10, End = 12, Text = "trecho removido" }]
            }
        };

        var plan = _service.Plan(clip, []);

        Assert.True(plan.Applied);
        Assert.Single(plan.Cuts);
        Assert.Equal(110, plan.Cuts[0].Start, 3);
        Assert.Equal(112, plan.Cuts[0].End, 3);
        Assert.Equal(68, plan.FinalDuration, 3);
        Assert.Contains("cortes manuais", plan.Reason);
    }

    [Fact]
    public void Plan_MantemCortesConsecutivosSemSomarTempoDuasVezes()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 70,
            SilenceTrimmingEnabled = false,
            SubtitleTrack = new SubtitleTrack
            {
                VideoCuts =
                [
                    new TranscriptCut { Start = 10, End = 12, Text = "primeira frase" },
                    new TranscriptCut { Start = 12, End = 14, Text = "segunda frase" }
                ]
            }
        };

        var plan = _service.Plan(clip, []);

        Assert.Equal(2, plan.Cuts.Count);
        Assert.Equal(4, plan.RemovedDuration, 3);
        Assert.Equal(66, plan.FinalDuration, 3);
        Assert.Equal("select='not(between(t\\,10\\,12)+between(t\\,12\\,14))',setpts=N/FRAME_RATE/TB,", SilenceTrimmingService.VideoPrefix(plan, 0));
        Assert.Equal("aselect='not(between(t\\,10\\,12)+between(t\\,12\\,14))',asetpts=N/SR/TB,", SilenceTrimmingService.AudioPrefix(plan, 0));
    }

    [Fact]
    public void Fingerprint_MudaQuandoTranscriptEditorAlteraOCorte()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 65,
            SubtitleTrack = new SubtitleTrack { Enabled = true }
        };
        var before = RenderStateService.Fingerprint(clip);

        clip.SubtitleTrack.VideoCuts.Add(new TranscriptCut { Start = 4, End = 5.5, Text = "remover" });
        var after = RenderStateService.Fingerprint(clip);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Plan_RecortaValoresForaDaDuracaoSemEliminarTodoOVideo()
    {
        var clip = new ClipCandidate
        {
            Start = 30,
            End = 40,
            SilenceTrimmingEnabled = false,
            SubtitleTrack = new SubtitleTrack
            {
                VideoCuts = [new TranscriptCut { Start = -5, End = 50, Text = "tudo" }]
            }
        };

        var plan = _service.Plan(clip, []);

        Assert.Single(plan.Cuts);
        Assert.Equal(30, plan.Cuts[0].Start, 3);
        Assert.Equal(39, plan.Cuts[0].End, 3);
        Assert.Equal(1, plan.FinalDuration, 3);
    }
}