using System.Globalization;
using System.Text.RegularExpressions;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed partial class VideoEnhancementService(ToolService tools, ILogger<VideoEnhancementService> logger)
{
    public async Task<VideoAnalysis> AnalyzeAsync(string media, double start, double duration, CancellationToken ct = default)
    {
        try
        {
            var output = await tools.CaptureDiagnosticAsync(tools.Find("ffmpeg"), ["-hide_banner", "-ss", Number(start), "-t", Number(Math.Min(duration, 10)), "-i", media, "-an", "-vf", "fps=1,scale=320:-2,signalstats,metadata=print", "-f", "null", "-"], Path.GetDirectoryName(media), ct);
            return Classify(Average(YavgRegex(), output), Average(YlowRegex(), output), Average(YhighRegex(), output), Average(SatavgRegex(), output));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Video] Analise visual indisponivel; preservando imagem original");
            return Classify(null, null, null, null);
        }
    }

    public static VideoAnalysis Classify(double? lumaAverage, double? lumaLow, double? lumaHigh, double? saturationAverage)
    {
        var contrast = lumaHigh - lumaLow;
        var kind = VideoEnhancementKind.Neutral; var reason = "Imagem dentro da faixa segura";
        if (lumaAverage is > 205) { kind = VideoEnhancementKind.Overexposed; reason = "Brilho medio excessivo"; }
        else if (lumaAverage is < 58) { kind = VideoEnhancementKind.Dark; reason = "Baixa luminosidade media"; }
        else if (lumaAverage is > 165 && saturationAverage is < 28 && contrast is < 105) { kind = VideoEnhancementKind.WashedOut; reason = "Imagem clara, pouco saturada e sem contraste"; }
        else if (contrast is < 82) { kind = VideoEnhancementKind.LowContrast; reason = "Faixa tonal estreita"; }
        else if (saturationAverage is < 22) { kind = VideoEnhancementKind.LowSaturation; reason = "Saturacao abaixo da faixa natural"; }
        return new VideoAnalysis { LumaAverage = lumaAverage, LumaLow = lumaLow, LumaHigh = lumaHigh, SaturationAverage = saturationAverage, Kind = kind, Reason = reason };
    }

    public static VideoEnhancementProfile CreateProfile(VideoAnalysis analysis) => new()
    {
        Kind = analysis.Kind,
        Filter = analysis.Kind switch
        {
            VideoEnhancementKind.Dark => "eq=brightness=0.035:contrast=1.035:gamma=1.045:saturation=1.02",
            VideoEnhancementKind.LowSaturation => "eq=saturation=1.07:contrast=1.015",
            VideoEnhancementKind.LowContrast => "eq=contrast=1.055:saturation=1.025,unsharp=5:5:0.18:5:5:0",
            VideoEnhancementKind.Noisy => "hqdn3d=1.1:1.1:2.2:2.2,unsharp=5:5:0.12:5:5:0",
            VideoEnhancementKind.WashedOut => "eq=contrast=1.06:brightness=-0.018:saturation=1.055",
            VideoEnhancementKind.Overexposed => "eq=brightness=-0.04:contrast=1.025:gamma=0.975:saturation=1.01",
            _ => "null"
        }
    };

    private static double? Average(Regex regex, string value)
    {
        var values = regex.Matches(value).Select(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? (double?)parsed : null).Where(item => item.HasValue).Select(item => item!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    [GeneratedRegex(@"lavfi\.signalstats\.YAVG=([0-9.]+)")] private static partial Regex YavgRegex();
    [GeneratedRegex(@"lavfi\.signalstats\.YLOW=([0-9.]+)")] private static partial Regex YlowRegex();
    [GeneratedRegex(@"lavfi\.signalstats\.YHIGH=([0-9.]+)")] private static partial Regex YhighRegex();
    [GeneratedRegex(@"lavfi\.signalstats\.SATAVG=([0-9.]+)")] private static partial Regex SatavgRegex();
}
