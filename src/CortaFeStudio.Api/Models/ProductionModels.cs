namespace CortaFeStudio.Api.Models;

public enum ProductionStatus
{
    Queued,
    Analyzing,
    Rendering,
    QualityCheck,
    AwaitingApproval,
    Ready,
    Scheduled,
    Published,
    Failed,
    Cancelled
}

public sealed class ProductionSettings
{
    public int CandidateCount { get; set; } = 20;
    public int FinalVideoCount { get; set; } = 10;
    public int VariantCount { get; set; } = 1;
    public int PostsPerDay { get; set; } = 2;
    public List<string> PostingTimes { get; set; } = ["12:00", "19:00"];
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public double MinimumSocialScore { get; set; } = 75;
    public List<SocialPlatform> Platforms { get; set; } = [SocialPlatform.TikTok];
    public bool AutoRender { get; set; }
    public bool AutoApprove { get; set; }
    public bool AutoSchedule { get; set; }
    public bool AutoPublish { get; set; }
}

public sealed class ProductionItem
{
    public string ClipId { get; set; } = "";
    public string Title { get; set; } = "";
    public double SocialScore { get; set; }
    public bool Approved { get; set; }
    public bool Rendered { get; set; }
    public int? QualityScore { get; set; }
    public QualityStatus? QualityStatus { get; set; }
    public List<ProductionPublication> Publications { get; set; } = [];
}

public sealed class ProductionPublication
{
    public SocialPlatform Platform { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string Status { get; set; } = "planned";
    public string? PublicationId { get; set; }
}

public sealed class ProductionBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Nova producao";
    public string SourceUrl { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public ProductionStatus Status { get; set; } = ProductionStatus.Queued;
    public int Progress { get; set; }
    public string Stage { get; set; } = "Na fila de producao";
    public string? Error { get; set; }
    public ProductionSettings Settings { get; set; } = new();
    public List<ProductionItem> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class CreateProductionBatchRequest
{
    public string Url { get; set; } = "";
    public string? Name { get; set; }
    public string ContentType { get; set; } = "pregacao";
    public string? Topic { get; set; }
    public string WhisperModel { get; set; } = "base";
    public ProductionSettings? Settings { get; set; }
}

public sealed record ProductionApprovalRequest(List<string> ClipIds, bool Render = true, bool Schedule = false);
