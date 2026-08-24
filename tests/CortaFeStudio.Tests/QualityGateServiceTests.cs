using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class QualityGateServiceTests
{
    [Fact]
    public void Evaluate_AprovaArquivoProfissional()
    {
        var report = QualityGateService.Evaluate(ValidFacts(), ValidClip(), true);
        Assert.Equal(QualityStatus.Pass, report.Status);
        Assert.Equal(100, report.Score);
        Assert.All(report.Checks, check => Assert.Equal(QualityStatus.Pass, check.Status));
    }

    [Fact]
    public void Evaluate_BloqueiaArquivoSemAudio()
    {
        var facts = ValidFacts(); facts.AudioCodec = "";
        var report = QualityGateService.Evaluate(facts, ValidClip(), true);

        Assert.Equal(QualityStatus.Blocked, report.Status);
        Assert.Contains(report.Checks, check => check.Code == "audio" && check.Status == QualityStatus.Blocked);
        Assert.True(report.CanAutoRepair);
    }

    [Fact]
    public void Evaluate_AlertaLoudnessECapaSemBloquear()
    {
        var facts = ValidFacts(); facts.LoudnessLufs = -21;
        var report = QualityGateService.Evaluate(facts, ValidClip(), false);

        Assert.Equal(QualityStatus.Warning, report.Status);
        Assert.Contains(report.Checks, check => check.Code == "loudness" && check.Status == QualityStatus.Warning);
        Assert.Contains(report.Checks, check => check.Code == "cover" && check.Status == QualityStatus.Warning);
    }

    [Fact]
    public void Evaluate_BloqueiaMetadadosOuSafeZoneInvalidos()
    {
        var clip = ValidClip(); clip.Title = ""; clip.SubtitleStyle = "fora-do-quadro"; clip.Hashtags = ["viral"];
        var report = QualityGateService.Evaluate(ValidFacts(), clip, true);
        Assert.Equal(QualityStatus.Blocked, report.Status);
        Assert.Contains(report.Checks, check => check.Code == "subtitle" && check.Status == QualityStatus.Blocked);
        Assert.Contains(report.Checks, check => check.Code == "hashtags" && check.Status == QualityStatus.Blocked);
    }

    private static QualityMediaFacts ValidFacts() => new() { FileExists = true, Opens = true, Duration = 65, Width = 1080, Height = 1920, VideoCodec = "h264", AudioCodec = "aac", Fps = 30, LoudnessLufs = -16, TruePeakDb = -1.5, LongestSilence = .7, LongestBlackFrame = .2 };
    private static ClipCandidate ValidClip() => new() { Title = "Uma mensagem forte", Caption = "Assista e compartilhe.", Hashtags = ["#mensagem", "#fe"], OutputPreset = "vertical", SubtitleStyle = "impact" };
}
