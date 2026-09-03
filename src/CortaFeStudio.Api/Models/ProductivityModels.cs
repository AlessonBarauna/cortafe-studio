namespace CortaFeStudio.Api.Models;

public sealed class EditingTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SubtitleStyle { get; set; } = "impact";
    public string TransitionStyle { get; set; } = "smooth";
    public string LayoutMode { get; set; } = "fill";
    public bool SilenceTrimmingEnabled { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1;
    public bool BrandFrameEnabled { get; set; } = true;
    public bool WatermarkEnabled { get; set; } = true;
}

public sealed class BrandKit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "Nova marca";
    public string Theme { get; set; } = "amado-jesus";
    public string Accent { get; set; } = "#C7A35A";
    public bool BrandFrameEnabled { get; set; } = true;
    public bool WatermarkEnabled { get; set; } = true;
    public string WatermarkText { get; set; } = "AJ  |  AMADO JESUS";
    public double WatermarkOpacity { get; set; } = .82;
    public string DefaultSubtitleStyle { get; set; } = "sermon";
}

public sealed class BatchClipStyleRequest
{
    public List<string> ClipIds { get; set; } = [];
    public string? TemplateId { get; set; }
    public string? BrandKitId { get; set; }
}

public sealed class BrandKitUpsertRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = "Nova marca";
    public string Theme { get; set; } = "amado-jesus";
    public string Accent { get; set; } = "#C7A35A";
    public bool BrandFrameEnabled { get; set; } = true;
    public bool WatermarkEnabled { get; set; } = true;
    public string WatermarkText { get; set; } = "AJ  |  AMADO JESUS";
    public double WatermarkOpacity { get; set; } = .82;
    public string DefaultSubtitleStyle { get; set; } = "sermon";
}
