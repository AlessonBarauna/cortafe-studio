using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class RenderFilterFactory
{
    public const string ProfessionalAudio =
        "highpass=f=70," +
        "lowpass=f=15000," +
        "afftdn=nf=-28," +
        "acompressor=threshold=0.125:ratio=2.5:attack=20:release=180:makeup=1.35," +
        "loudnorm=I=-16:LRA=9:TP=-1.5," +
        "alimiter=limit=0.95:attack=5:release=50:level=disabled," +
        "afade=t=in:st=0:d=0.12";

    public static string Framing(ClipCandidate clip)
    {
        var (width, height) = Dimensions(clip.OutputPreset);
        var baseFraming = clip.LayoutMode == "blur"
            ? $"split=2[bg][fg];[bg]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},gblur=sigma=34,eq=brightness=-0.18[back];[fg]scale={width}:-2[front];[back][front]overlay=(W-w)/2:(H-h)/2"
            : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}:{CropX(clip, width)}:{CropY(clip.CropFocus, height)}";

        var moments = PunchInPlanner.Plan(clip);
        return moments.Count == 0 ? $"{baseFraming},{SignatureMotion(width, height)}" : $"{baseFraming},{PunchIn(moments, width, height)}";
    }

    public static string PunchIn(IReadOnlyList<PunchInMoment> moments, int width, int height)
    {
        if (moments.Count == 0) return "null";
        var expression = "1";
        foreach (var moment in moments.Take(3))
        {
            var start = Scalar(moment.Start);
            var end = Scalar(moment.End);
            var amount = Scalar(Math.Clamp(moment.Scale - 1, .02, .08));
            var duration = Scalar(Math.Max(.2, moment.End - moment.Start));
            expression += $"+if(between(t\\,{start}\\,{end})\\,{amount}*sin(PI*(t-{start})/{duration})\\,0)";
        }

        return $"scale=w='trunc(iw*({expression})/2)*2':h='trunc(ih*({expression})/2)*2':eval=frame," +
               $"crop={width}:{height}:(iw-{width})/2:(ih-{height})/2";
    }

    public static string Branding(ClipCandidate clip, string escapedWatermarkFile, string font)
    {
        var theme = clip.BrandTheme switch
        {
            "worship" => (Accent: "0xB98CFF", Panel: "0x130D24"),
            "podcast" => (Accent: "0xFF5A5F", Panel: "0x171119"),
            _ => (Accent: "0xC7A35A", Panel: "0x17120E")
        };
        var filters = new List<string>();
        if (clip.BrandFrameEnabled)
        {
            filters.Add($"drawbox=x=24:y=24:w=iw-48:h=ih-48:color={theme.Accent}@0.42:t=3");
            filters.Add($"drawbox=x=0:y=h-118:w=iw:h=118:color={theme.Panel}@0.72:t=fill");
            filters.Add($"drawbox=x=0:y=h-120:w=iw:h=2:color={theme.Accent}@0.72:t=fill");
        }
        if (clip.WatermarkEnabled && !string.IsNullOrWhiteSpace(clip.WatermarkText))
        {
            var opacity = Math.Clamp(clip.WatermarkOpacity, .1, 1).ToString("0.##", CultureInfo.InvariantCulture);
            var position = "x=(w-text_w)/2:y=h-text_h-42";
            filters.Add($"drawtext=textfile='{escapedWatermarkFile}'{font}:fontsize=32:fontcolor=black@0.72:borderw=2:bordercolor=black@0.45:{position.Replace("/2", "/2+3").Replace("-42", "-39")}");
            filters.Add($"drawtext=textfile='{escapedWatermarkFile}'{font}:fontsize=32:fontcolor=0xFFF4D6@0.32:borderw=0:{position.Replace("/2", "/2-1").Replace("-42", "-43")}");
            filters.Add($"drawtext=textfile='{escapedWatermarkFile}'{font}:fontsize=32:fontcolor={theme.Accent}@{opacity}:borderw=1:bordercolor=0x3A2A18@0.85:{position}");
        }
        return string.Join(',', filters);
    }

    public static string SignatureMotion(int width, int height) =>
        "scale=w='trunc(iw*(1+if(between(t\\,0.2\\,2.8)\\,0.026*sin(PI*(t-0.2)/2.6)\\,0))/2)*2':" +
        "h='trunc(ih*(1+if(between(t\\,0.2\\,2.8)\\,0.026*sin(PI*(t-0.2)/2.6)\\,0))/2)*2':eval=frame," +
        $"crop={width}:{height}:(iw-{width})/2:(ih-{height})/2";

    public static string CreativeLook(double duration)
    {
        var filters = new List<string>
        {
            "colorbalance=rs=0.025:gs=0.008:bs=-0.028:rm=0.012:bm=-0.015",
            "eq=contrast=1.045:saturation=0.93:gamma=0.99",
            "vignette=PI/8",
            "noise=alls=1.2:allf=t"
        };
        if (duration > .6)
        {
            filters.Add("fade=t=in:st=0:d=0.22");
            filters.Add($"fade=t=out:st={Scalar(Math.Max(.22, duration - .28))}:d=0.28");
        }
        return string.Join(',', filters);
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
