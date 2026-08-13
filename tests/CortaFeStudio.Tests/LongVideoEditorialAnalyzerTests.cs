using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class LongVideoEditorialAnalyzerTests
{
    [Fact]
    public void BuildChunks_VideoLongoUsaJanelasComSobreposicao()
    {
        var transcript = Enumerable.Range(0, 200).Select(index => new TranscriptSegment { Start = index * 20, End = index * 20 + 19, Text = $"Trecho {index}." }).ToList();
        var chunks = LongVideoEditorialAnalyzer.BuildChunks(transcript, 4000);
        Assert.True(chunks.Count >= 4);
        Assert.True(chunks[0].Max(segment => segment.End) >= chunks[1].Min(segment => segment.Start));
        Assert.All(chunks, chunk => Assert.NotEmpty(chunk));
    }

    [Fact]
    public void BuildChunks_VideoCurtoPermaneceEmUmBloco()
    {
        var transcript = new List<TranscriptSegment> { new() { Start = 0, End = 600, Text = "Mensagem." } };
        Assert.Single(LongVideoEditorialAnalyzer.BuildChunks(transcript, 600));
    }
}
