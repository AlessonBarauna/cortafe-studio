using System.Globalization;
using System.Text.RegularExpressions;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed partial class AudioAnalyzer(ToolService tools, ILogger<AudioAnalyzer> logger)
{
    public async Task<AudioAnalysis> AnalyzeAsync(string media, double start, double duration, string contentType, CancellationToken ct = default)
    {
        try
        {
            var output = await tools.CaptureDiagnosticAsync(tools.Find("ffmpeg"), ["-hide_banner", "-ss", Number(start), "-t", Number(Math.Min(duration, 30)), "-i", media, "-vn", "-af", "volumedetect,silencedetect=noise=-38dB:d=0.7", "-f", "null", "-"], Path.GetDirectoryName(media), ct);
            var mean = Read(MeanVolumeRegex(), output); var peak = Read(MaxVolumeRegex(), output);
            var silenceSeconds = SilenceDurationRegex().Matches(output).Sum(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0);
            return Classify(contentType, mean, peak, Math.Clamp(silenceSeconds / Math.Max(1, Math.Min(duration, 30)), 0, 1));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Audio] Analise indisponivel; usando fallback para {ContentType}", contentType);
            return Classify(contentType, null, null, 0);
        }
    }

    public static AudioAnalysis Classify(string contentType, double? meanVolumeDb, double? peakVolumeDb, double silenceRatio)
    {
        var normalized = contentType.Trim().ToLowerInvariant();
        var profile = normalized == "louvor" ? AudioProfile.Worship : normalized == "podcast" ? AudioProfile.Podcast : AudioProfile.VoiceClean;
        var reason = "Perfil editorial e dinamica preservados";
        if (peakVolumeDb is >= -.2) { profile = AudioProfile.Clipped; reason = "Picos proximos de 0 dB"; }
        else if (meanVolumeDb is < -28) { profile = AudioProfile.LowVolume; reason = "Volume medio abaixo de -28 dB"; }
        else if (profile == AudioProfile.VoiceClean && silenceRatio > .18) { profile = AudioProfile.VoiceNoisy; reason = "Ruido ou pausas acima do esperado"; }
        return new AudioAnalysis { Profile = profile, MeanVolumeDb = meanVolumeDb, PeakVolumeDb = peakVolumeDb, SilenceRatio = silenceRatio, Reason = reason };
    }

    private static double? Read(Regex regex, string value) => regex.Match(value) is { Success: true } match && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    [GeneratedRegex(@"mean_volume:\s*(-?[0-9.]+) dB", RegexOptions.IgnoreCase)] private static partial Regex MeanVolumeRegex();
    [GeneratedRegex(@"max_volume:\s*(-?[0-9.]+) dB", RegexOptions.IgnoreCase)] private static partial Regex MaxVolumeRegex();
    [GeneratedRegex(@"silence_duration:\s*([0-9.]+)", RegexOptions.IgnoreCase)] private static partial Regex SilenceDurationRegex();
}

public static class AudioFilterFactory
{
    public static AudioProcessingProfile Create(AudioAnalysis analysis, double duration)
    {
        var core = analysis.Profile switch
        {
            AudioProfile.Worship or AudioProfile.Music => "highpass=f=35,lowpass=f=19000,acompressor=threshold=0.18:ratio=1.45:attack=35:release=300:makeup=1.08,loudnorm=I=-14:LRA=12:TP=-1.2",
            AudioProfile.Podcast => "highpass=f=65,lowpass=f=16000,equalizer=f=3200:t=q:w=1.1:g=1.2,acompressor=threshold=0.14:ratio=2.2:attack=18:release=160:makeup=1.25,loudnorm=I=-16:LRA=8:TP=-1.5",
            AudioProfile.VoiceNoisy => "highpass=f=80,lowpass=f=14500,afftdn=nf=-32,equalizer=f=2800:t=q:w=1.2:g=1,acompressor=threshold=0.13:ratio=2.35:attack=20:release=190:makeup=1.3,loudnorm=I=-16:LRA=9:TP=-1.5",
            AudioProfile.LowVolume => "highpass=f=70,lowpass=f=15500,acompressor=threshold=0.1:ratio=2.6:attack=20:release=180:makeup=1.6,loudnorm=I=-16:LRA=8:TP=-1.5",
            AudioProfile.Clipped => "highpass=f=70,lowpass=f=15500,acompressor=threshold=0.18:ratio=1.8:attack=8:release=220:makeup=1,loudnorm=I=-17:LRA=10:TP=-2",
            AudioProfile.VoiceWithMusic => "highpass=f=55,lowpass=f=17500,acompressor=threshold=0.17:ratio=1.6:attack=30:release=260:makeup=1.1,loudnorm=I=-15:LRA=11:TP=-1.3",
            _ => "highpass=f=70,lowpass=f=15500,afftdn=nf=-35,equalizer=f=3000:t=q:w=1.2:g=1,acompressor=threshold=0.125:ratio=2.3:attack=20:release=180:makeup=1.3,loudnorm=I=-16:LRA=9:TP=-1.5"
        };
        var end = Math.Max(.12, duration - .18).ToString("0.###", CultureInfo.InvariantCulture);
        return new AudioProcessingProfile { Profile = analysis.Profile, TargetLoudness = analysis.Profile is AudioProfile.Worship or AudioProfile.Music ? "-14 LUFS" : "-16 LUFS", Filter = $"{core},alimiter=limit=0.95:attack=5:release=50:level=disabled,afade=t=in:st=0:d=0.12,afade=t=out:st={end}:d=0.18" };
    }
}
