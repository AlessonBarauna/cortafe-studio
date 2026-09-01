namespace CortaFeStudio.Api.Models;

public sealed class YouTubeMetadata
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    public string CallToAction { get; set; } = "";
}

public sealed class InstagramMetadata
{
    public string FirstLine { get; set; } = "";
    public string Caption { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    public string CallToAction { get; set; } = "";
}

public sealed class TikTokMetadata
{
    public string Caption { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    public string CallToAction { get; set; } = "";
}

public sealed class PlatformMetadata
{
    public YouTubeMetadata YouTube { get; set; } = new();
    public InstagramMetadata Instagram { get; set; } = new();
    public TikTokMetadata TikTok { get; set; } = new();
    public string RecommendedHook { get; set; } = "";
    public List<string> HookOptions { get; set; } = [];
    public double CopyScore { get; set; }
    public List<string> CopyWarnings { get; set; } = [];
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
