using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class RenderFilterFactory
{
    public const string ProfessionalAudio = "highpass=f=70,lowpass=f=15000,afftdn=nf=-25,loudnorm=I=-16:LRA=11:TP=-1.5,afade=t=in:st=0:d=0.12";
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

    public static string Audio(double duration)
    {
        var fadeOut = Math.Max(.12, duration - .18).ToString("0.###", CultureInfo.InvariantCulture);
        return $"{ProfessionalAudio},afade=t=out:st={fadeOut}:d=0.18";
    }
}
