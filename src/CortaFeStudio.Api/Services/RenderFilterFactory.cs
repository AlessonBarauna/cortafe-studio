using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class RenderFilterFactory
{
    public const string ProfessionalAudio = "highpass=f=70,lowpass=f=15000,afftdn=nf=-25,loudnorm=I=-16:LRA=11:TP=-1.5,afade=t=in:st=0:d=0.12";
    public static string Framing(ClipCandidate clip)
    {
        var (width, height) = Dimensions(clip.OutputPreset);
        return clip.LayoutMode == "blur"
            ? $"split=2[bg][fg];[bg]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},gblur=sigma=34,eq=brightness=-0.18[back];[fg]scale={width}:-2[front];[back][front]overlay=(W-w)/2:(H-h)/2"
            : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}:{CropX(clip, width)}:{CropY(clip.CropFocus, height)}";
    }

    public static (int Width, int Height) Dimensions(string? preset) => preset switch
    {
        "portrait" => (1080, 1350),
        "square" => (1080, 1080),
        "landscape" => (1920, 1080),
        _ => (1080, 1920)
    };

    public static string CropX(ClipCandidate clip, int width = 1080) => clip.FramingTrack.Count > 1
        ? $"max(0\\,min(iw-{width}\\,iw*({TrackingExpression(clip.FramingTrack)})-{width / 2}))"
        : CropX(clip.CropX, width);

    public static string CropX(double focus, int width = 1080) =>
        $"max(0\\,min(iw-{width}\\,iw*{Math.Clamp(focus, 0, 1).ToString("0.###", CultureInfo.InvariantCulture)}-{width / 2}))";

    private static string TrackingExpression(IReadOnlyList<FramingKeyframe> source)
    {
        var points = source.OrderBy(point => point.Time).Take(30).ToList();
        var expression = Number(points[^1].X);
        for (var index = points.Count - 2; index >= 0; index--)
        {
            var current = points[index]; var next = points[index + 1];
            var duration = Math.Max(.001, next.Time - current.Time);
            var interpolation = $"{Number(current.X)}+({Number(next.X)}-{Number(current.X)})*(t-{Scalar(current.Time)})/{Scalar(duration)}";
            expression = $"if(lte(t\\,{Scalar(next.Time)})\\,{interpolation}\\,{expression})";
        }
        return expression;
    }

    private static string Number(double value) => Math.Clamp(value, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Scalar(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string CropY(string focus, int height = 1920) => focus switch
    {
        "top" => "0",
        "bottom" => $"ih-{height}",
        _ => $"(ih-{height})/2"
    };

    public static string Audio(double duration)
    {
        var fadeOut = Math.Max(.12, duration - .18).ToString("0.###", CultureInfo.InvariantCulture);
        return $"{ProfessionalAudio},afade=t=out:st={fadeOut}:d=0.18";
    }
}
