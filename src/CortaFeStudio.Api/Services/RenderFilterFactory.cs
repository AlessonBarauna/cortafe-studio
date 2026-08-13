using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class RenderFilterFactory
{
    public static string Framing(ClipCandidate clip) => clip.LayoutMode == "blur"
        ? "split=2[bg][fg];[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,gblur=sigma=34,eq=brightness=-0.18[back];[fg]scale=1080:-2[front];[back][front]overlay=(W-w)/2:(H-h)/2"
        : $"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:{CropX(clip.CropX)}:{CropY(clip.CropFocus)}";

    public static string CropX(double focus) =>
        $"max(0\\,min(iw-1080\\,iw*{Math.Clamp(focus, 0, 1).ToString("0.###", CultureInfo.InvariantCulture)}-540))";

    public static string CropY(string focus) => focus switch
    {
        "top" => "0",
        "bottom" => "ih-1920",
        _ => "(ih-1920)/2"
    };
}
