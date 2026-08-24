namespace CortaFeStudio.Api.Models;

public sealed class PerformanceSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public long Views { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public double? WatchTimeSeconds { get; set; }
    public double? RetentionPercent { get; set; }
}

public sealed class ContentPerformance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string ProjectId { get; set; } = "";
    public string ClipId { get; set; } = "";
    public string? VariantId { get; set; }
    public SocialPlatform Platform { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public double Duration { get; set; }
    public string EditorialProfile { get; set; } = "";
    public string SubtitleStyle { get; set; } = "";
    public string Topic { get; set; } = "";
    public double HookScore { get; set; }
    public List<PerformanceSnapshot> Snapshots { get; set; } = [];
}

public sealed class PerformanceInsights
{
    public int Samples { get; set; }
    public double? PreferredDuration { get; set; }
    public string? BestTopic { get; set; }
    public string? BestSubtitleStyle { get; set; }
    public string? BestEditorialProfile { get; set; }
    public SocialPlatform? BestPlatform { get; set; }
    public int? BestPublishingHour { get; set; }
    public double? RecommendedHookScore { get; set; }
    public List<string> Recommendations { get; set; } = [];
}

public sealed class RecordPerformanceRequest
{
    public string ProjectId { get; set; } = "";
    public string ClipId { get; set; } = "";
    public string? VariantId { get; set; }
    public SocialPlatform Platform { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public PerformanceSnapshot Snapshot { get; set; } = new();
}
