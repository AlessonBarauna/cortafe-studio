namespace CortaFeStudio.Api.Models;

public enum SocialPlatform { YouTube, Instagram, TikTok }
public sealed class SocialCredential
{
    public SocialPlatform Platform { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? PublicBaseUrl { get; set; }
}
public sealed record SocialConfigurationRequest(SocialPlatform Platform, string ClientId, string ClientSecret, string? PublicBaseUrl);
public sealed record PublishRequest(SocialPlatform Platform, string Title, string Description, string Privacy = "private", DateTimeOffset? PublishAt = null);
public sealed class TikTokCreatorInfo
{
    public string? Username { get; set; }
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public List<string> PrivacyLevelOptions { get; set; } = [];
    public bool CommentDisabled { get; set; }
    public bool DuetDisabled { get; set; }
    public bool StitchDisabled { get; set; }
    public int MaxVideoPostDurationSec { get; set; } = 180;
}
public sealed class PublicationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public SocialPlatform Platform { get; set; }
    public string ProjectId { get; set; } = "";
    public string ClipId { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Privacy { get; set; } = "private";
    public string? UploadSessionUrl { get; set; }
    public long UploadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int Progress { get; set; }
    public string? PlatformStatus { get; set; }
}
