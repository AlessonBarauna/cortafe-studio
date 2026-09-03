using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class EditorialIntelligencePipelineTests
{
    [Fact]
    public void BuildTopicChunks_UsesSemanticTimelineWhenAvailable()
    {
        var transcript = Enumerable.Range(0, 12)
            .Select(index => new TranscriptSegment
            {
                Start = index * 30,
                End = index * 30 + 25,
                Text = $"segmento {index}"
            })
            .ToList();

        var topics = new List<EditorialTopic>
        {
            new() { Title = "Perdão", Start = 0, End = 145, Confidence = .9 },
            new() { Title = "Confiança", Start = 150, End = 330, Confidence = .9 }
        };

        var chunks = EditorialIntelligencePipeline.BuildTopicChunks(transcript, topics, 1200, 60);

        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks[0], segment => segment.Start == 0);
        Assert.Contains(chunks[1], segment => segment.Start >= 150);
    }

    [Fact]
    public void BuildTopicChunks_FallsBackToTimeChunksWithoutTopics()
    {
        var transcript = Enumerable.Range(0, 20)
            .Select(index => new TranscriptSegment
            {
                Start = index * 60,
                End = index * 60 + 45,
                Text = $"segmento {index}"
            })
            .ToList();

        var chunks = EditorialIntelligencePipeline.BuildTopicChunks(transcript, [], 600, 60);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, chunk => Assert.NotEmpty(chunk));
    }
}
