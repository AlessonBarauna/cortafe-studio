using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed partial class QualityGateService(ToolService tools, ProjectStore projects, ILogger<QualityGateService> logger)
{
    public async Task<QualityReport> ValidateAsync(VideoProject project, ClipCandidate clip, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(clip.VideoPath) ? null : projects.ResolveAsset(project.Id, clip.VideoPath);
        var facts = path is null ? new QualityMediaFacts() : await InspectAsync(path, ct);
        var report = Evaluate(facts, clip, path is not null && !string.IsNullOrWhiteSpace(clip.CoverPath) && projects.ResolveAsset(project.Id, clip.CoverPath) is not null);
        clip.QualityReport = report; await projects.SaveAsync(project);
        logger.LogInformation("[Quality] project={ProjectId} clip={ClipId} score={Score} status={Status}", project.Id, clip.Id, report.Score, report.Status);
        return report;
    }

    public async Task<QualityMediaFacts> InspectAsync(string path, CancellationToken ct = default)
    {
        var facts = new QualityMediaFacts { FileExists = File.Exists(path) };
        if (!facts.FileExists) return facts;
        try
        {
            var raw = await tools.CaptureAsync(tools.Find("ffprobe"), ["-v", "error", "-show_streams", "-show_format", "-of", "json", path], Path.GetDirectoryName(path), ct);
            using var document = JsonDocument.Parse(raw); var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToList();
            var video = streams.FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
            var audio = streams.FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio");
            facts.Opens = video.ValueKind != JsonValueKind.Undefined;
            facts.VideoCodec = Text(video, "codec_name"); facts.Width = Integer(video, "width"); facts.Height = Integer(video, "height"); facts.Fps = Rate(Text(video, "avg_frame_rate"));
            facts.AudioCodec = Text(audio, "codec_name");
            if (document.RootElement.TryGetProperty("format", out var format)) facts.Duration = Double(format, "duration");
            if (!string.IsNullOrWhiteSpace(facts.AudioCodec))
            {
                var diagnostic = await tools.CaptureDiagnosticAsync(tools.Find("ffmpeg"), ["-hide_banner", "-i", path, "-af", "ebur128=peak=true,silencedetect=noise=-38dB:d=0.7", "-vf", "blackdetect=d=0.5:pix_th=0.10", "-f", "null", "-"], Path.GetDirectoryName(path), ct);
                facts.LoudnessLufs = Last(LoudnessRegex(), diagnostic); facts.TruePeakDb = Last(TruePeakRegex(), diagnostic);
                facts.LongestSilence = Maximum(SilenceRegex(), diagnostic); facts.LongestBlackFrame = Maximum(BlackRegex(), diagnostic);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "[Quality] Nao foi possivel inspecionar {File}", Path.GetFileName(path)); }
        return facts;
    }

    public static QualityReport Evaluate(QualityMediaFacts facts, ClipCandidate clip, bool coverExists)
    {
        var checks = new List<QualityCheck>();
        Add("file", "Arquivo existe", facts.FileExists, "Arquivo renderizado nao encontrado", true);
        Add("opens", "Arquivo abre", facts.Opens, "FFprobe nao conseguiu abrir o arquivo", true);
        Add("duration", "Duracao valida", facts.Duration is >= 1 and <= 180, $"Duracao: {facts.Duration:0.0}s", true);
        Add("resolution", "Resolucao valida", facts.Width >= 720 && facts.Height >= 720, $"{facts.Width}x{facts.Height}", true);
        Add("video_codec", "Codec de video", facts.VideoCodec == "h264", string.IsNullOrWhiteSpace(facts.VideoCodec) ? "Sem video" : facts.VideoCodec, true);
        Add("audio", "Audio presente", !string.IsNullOrWhiteSpace(facts.AudioCodec), "Faixa de audio ausente", true);
        Add("audio_codec", "Codec de audio", facts.AudioCodec == "aac", string.IsNullOrWhiteSpace(facts.AudioCodec) ? "Sem codec" : facts.AudioCodec, true);
        Add("fps", "FPS valido", facts.Fps is >= 20 and <= 61, $"{facts.Fps:0.##} fps", true);
        AddLevel("loudness", "Loudness", facts.LoudnessLufs, -28, -20, -12, -9, "LUFS");
        AddPeak("true_peak", "True peak", facts.TruePeakDb);
        AddDuration("silence", "Silencio longo", facts.LongestSilence, 3, 10, true);
        AddDuration("black", "Frame preto longo", facts.LongestBlackFrame, 1.5, 4, true);
        Add("subtitle", "Legenda na safe zone", SafeSubtitle(clip), "Preset ou estilo de legenda invalido", true);
        Add("title", "Titulo presente", !string.IsNullOrWhiteSpace(clip.Title), "Titulo vazio", false);
        Add("caption", "Caption presente", !string.IsNullOrWhiteSpace(clip.Caption), "Caption vazia", false);
        Add("hashtags", "Hashtags validas", clip.Hashtags.Count is > 0 and <= 15 && clip.Hashtags.All(tag => tag.StartsWith('#') && tag.Length is > 1 and <= 50), "Use de 1 a 15 hashtags validas", false);
        Add("cover", "Capa disponivel", coverExists, "Capa nao encontrada", true, QualityStatus.Warning);
        var status = checks.Any(check => check.Status == QualityStatus.Blocked) ? QualityStatus.Blocked : checks.Any(check => check.Status == QualityStatus.Warning) ? QualityStatus.Warning : QualityStatus.Pass;
        var score = Math.Clamp(100 - checks.Sum(check => check.Status == QualityStatus.Blocked ? 14 : check.Status == QualityStatus.Warning ? 4 : 0), 0, 100);
        return new QualityReport { Status = status, Score = score, Checks = checks };

        void Add(string code, string label, bool pass, string detail, bool repairable, QualityStatus failure = QualityStatus.Blocked) => checks.Add(new QualityCheck { Code = code, Label = label, Status = pass ? QualityStatus.Pass : failure, Detail = pass ? "OK" : detail, AutoRepairable = !pass && repairable });
        void AddLevel(string code, string label, double? value, double blockLow, double warnLow, double warnHigh, double blockHigh, string unit)
        {
            var status = value is null || value < blockLow || value > blockHigh ? QualityStatus.Blocked : value < warnLow || value > warnHigh ? QualityStatus.Warning : QualityStatus.Pass;
            checks.Add(new QualityCheck { Code = code, Label = label, Status = status, Detail = value is null ? "Medicao indisponivel" : $"{value:0.0} {unit}", AutoRepairable = status != QualityStatus.Pass });
        }
        void AddPeak(string code, string label, double? value) { var status = value is null || value > 0 ? QualityStatus.Blocked : value > -1 ? QualityStatus.Warning : QualityStatus.Pass; checks.Add(new QualityCheck { Code = code, Label = label, Status = status, Detail = value is null ? "Medicao indisponivel" : $"{value:0.0} dBTP", AutoRepairable = status != QualityStatus.Pass }); }
        void AddDuration(string code, string label, double value, double warning, double blocked, bool repairable) => checks.Add(new QualityCheck { Code = code, Label = label, Status = value >= blocked ? QualityStatus.Blocked : value >= warning ? QualityStatus.Warning : QualityStatus.Pass, Detail = $"{value:0.0}s", AutoRepairable = value >= blocked && repairable });
    }

    private static bool SafeSubtitle(ClipCandidate clip) => clip.OutputPreset is "vertical" or "portrait" or "square" or "landscape" && clip.SubtitleStyle is "impact" or "clean" or "bold" or "podcast" or "sermon" or "motivational" or "minimal" or "worship";
    private static string Text(JsonElement value, string name) => value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty(name, out var item) ? item.GetString() ?? "" : "";
    private static int Integer(JsonElement value, string name) => value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty(name, out var item) && item.TryGetInt32(out var result) ? result : 0;
    private static double Double(JsonElement value, string name) => value.TryGetProperty(name, out var item) && double.TryParse(item.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static double Rate(string value) { var parts = value.Split('/'); return parts.Length == 2 && double.TryParse(parts[0], out var top) && double.TryParse(parts[1], out var bottom) && bottom != 0 ? top / bottom : 0; }
    private static double? Last(Regex regex, string value) => regex.Matches(value).Select(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? (double?)parsed : null).LastOrDefault(item => item.HasValue);
    private static double Maximum(Regex regex, string value) => regex.Matches(value).Select(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0).DefaultIfEmpty().Max();
    [GeneratedRegex(@"I:\s*(-?[0-9.]+) LUFS")] private static partial Regex LoudnessRegex();
    [GeneratedRegex(@"Peak:\s*(-?[0-9.]+) dBFS")] private static partial Regex TruePeakRegex();
    [GeneratedRegex(@"silence_duration:\s*([0-9.]+)")] private static partial Regex SilenceRegex();
    [GeneratedRegex(@"black_duration:([0-9.]+)")] private static partial Regex BlackRegex();
}
