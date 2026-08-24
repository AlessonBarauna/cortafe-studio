using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ManualClipServiceTests
{
    private readonly ManualClipService _service = new();

    [Fact]
    public void Create_RecortaTranscricaoEMantemCorteSemRenderizar()
    {
        var project = ReadyProject();
        project.Transcript =
        [
            new TranscriptSegment { Start = 8, End = 12, Text = "fora antes" },
            new TranscriptSegment
            {
                Start = 12, End = 18, Text = "A esperança permanece.",
                Words =
                [
                    new TranscriptWord { Start = 12, End = 13, Word = "A" },
                    new TranscriptWord { Start = 13, End = 14, Word = "esperança" },
                    new TranscriptWord { Start = 14, End = 15, Word = "permanece." }
                ]
            },
            new TranscriptSegment { Start = 30, End = 34, Text = "fora depois" }
        ];

        var clip = _service.Create(project, 11.5, 18.25);

        Assert.Equal("manual", clip.Source);
        Assert.Equal("A esperança permanece.", clip.Transcript);
        Assert.Null(clip.VideoPath);
        Assert.Null(clip.CoverPath);
        Assert.Equal(11.5, clip.Start);
        Assert.Equal(18.25, clip.End);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(10, 10.5)]
    [InlineData(95, 101)]
    public void Create_RejeitaIntervaloInvalido(double start, double end)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.Create(ReadyProject(), start, end));
    }

    [Fact]
    public void Create_NaoImpõeDuracaoDoCorteAutomatico()
    {
        var clip = _service.Create(ReadyProject(), 5, 17);
        Assert.Equal(12, clip.End - clip.Start);
    }

    private static VideoProject ReadyProject() => new()
    {
        Status = ProjectStatus.Ready,
        LocalMedia = "source.mp4",
        Duration = 100,
        Options = new ProjectOptions { ContentType = "pregacao" }
    };
}
