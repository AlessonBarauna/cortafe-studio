namespace CortaFeStudio.Api.Models;

public sealed class RenderEncoderProfile
{
    public string Name { get; init; } = "CPU / libx264";
    public string Codec { get; init; } = "libx264";
    public List<string> Arguments { get; init; } = ["-preset", "medium", "-crf", "18"];
    public bool HardwareAccelerated { get; init; }
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}
