using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class RenderStateService
{
    public static string Fingerprint(ClipCandidate clip)
    {
        var state = new
        {
            clip.Start, clip.End, clip.Title, clip.CropFocus, clip.CropX, clip.FramingTrack, clip.TransitionStyle,
            clip.LayoutMode, clip.OutputPreset, clip.SubtitleStyle, clip.SubtitleTrack,
            clip.BrandFrameEnabled, clip.BrandTheme, clip.WatermarkEnabled, clip.WatermarkText, clip.WatermarkOpacity, clip.PlaybackSpeed, clip.SilenceTrimmingEnabled
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))))[..16];
    }

    public static void MarkIfChanged(ClipCandidate clip, string previousFingerprint)
    {
        if (!string.IsNullOrWhiteSpace(clip.VideoPath) && !string.Equals(previousFingerprint, Fingerprint(clip), StringComparison.Ordinal))
            clip.RenderOutdated = true;
    }
}
