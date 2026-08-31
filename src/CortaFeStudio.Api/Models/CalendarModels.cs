namespace CortaFeStudio.Api.Models;

public sealed class SchedulingStrategy
{
    public int PostsPerDay { get; set; } = 2;
    public List<string> PreferredTimes { get; set; } = ["10:00", "19:00"];
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int MinimumIntervalMinutes { get; set; } = 120;
}

public sealed class ScheduledContentItem
{
    public string? PublicationId { get; set; }
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ClipId { get; set; } = "";
    public string ClipTitle { get; set; } = "";
    public string EditorialProfile { get; set; } = "";
    public double SocialScore { get; set; }
    public SocialPlatform Platform { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string Status { get; set; } = "planned";
    public string? Error { get; set; }
}

public sealed class CreateCalendarRequest
{
    public string ProjectId { get; set; } = "";
    public List<string> ClipIds { get; set; } = [];
    public List<SocialPlatform> Platforms { get; set; } = [SocialPlatform.TikTok];
    public SchedulingStrategy Strategy { get; set; } = new();
}

public sealed record RescheduleContentRequest(DateTimeOffset ScheduledAt);
