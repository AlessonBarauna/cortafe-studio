using System.Globalization;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

/// <summary>
/// Pós-processamento animado do corte (legenda cinética, gancho, ilustrações, câmera e sons),
/// executado pelo módulo Python em scripts/fx. Quando indisponível, o render clássico continua valendo.
/// </summary>
public sealed class CortaFeFxService(ToolService tools)
{
    private bool available;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (available) return true;
        try
        {
            await tools.RunAsync(tools.Find("python"), ["-I", "-c", "import skia, numpy, scipy, PIL"], null, ct);
            available = File.Exists(ScriptPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { available = false; }
        return available;
    }

    public Task ApplyAsync(string video, string wordsFile, string output, string workDir, string? watermark, double watermarkOpacity, CancellationToken ct = default, bool sfx = true, bool hook = true, bool themes = true, double captionX = 50, double captionY = 72.7)
    {
        var args = new List<string> { "-I", ScriptPath, "--video", video, "--words", wordsFile, "--out", output, "--ffmpeg", tools.Find("ffmpeg"), "--ffprobe", tools.Find("ffprobe") };
        if (!sfx) args.Add("--no-sfx");
        if (!hook) args.Add("--no-hook");
        if (!themes) args.Add("--no-themes");
        args.AddRange(["--caption-x", Math.Clamp(captionX, 5, 95).ToString("0.##", CultureInfo.InvariantCulture), "--caption-y", Math.Clamp(captionY, 5, 95).ToString("0.##", CultureInfo.InvariantCulture)]);
        if (!string.IsNullOrWhiteSpace(watermark))
            args.AddRange(["--watermark", watermark, "--watermark-opacity", Math.Clamp(watermarkOpacity, .1, 1).ToString("0.##", CultureInfo.InvariantCulture)]);
        return tools.RunAsync(tools.Find("python"), args, workDir, ct);
    }

    private string ScriptPath => Path.Combine(tools.Root, "scripts", "fx", "cortafe_fx.py");

    /// <summary>Palavras do corte em segundos relativos ao início do vídeo renderizado (já considerando a velocidade).</summary>
    public static string WordsJson(IEnumerable<TranscriptSegment> segments, ClipCandidate clip, double speed)
    {
        speed = speed <= 0 ? 1 : speed;
        var length = (clip.End - clip.Start) / speed;
        var list = new List<object>();
        foreach (var segment in segments.Where(s => s.End >= clip.Start && s.Start <= clip.End))
        {
            var words = segment.Words.Where(w => !string.IsNullOrWhiteSpace(w.Word)).ToList();
            if (words.Count == 0)
            {
                var tokens = segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                var weights = tokens.Select(token => token.Length + 2.0).ToArray();
                var total = weights.Sum();
                var cursor = segment.Start;
                for (var i = 0; i < tokens.Length; i++)
                {
                    var end = cursor + (segment.End - segment.Start) * weights[i] / total;
                    words.Add(new TranscriptWord { Word = tokens[i], Start = cursor, End = end });
                    cursor = end;
                }
            }
            foreach (var word in words)
            {
                var start = (word.Start - clip.Start) / speed;
                var end = (word.End - clip.Start) / speed;
                if (end <= 0 || start >= length) continue;
                list.Add(new { word = word.Word.Trim(), start = Math.Max(0, start), end = Math.Min(length, Math.Max(end, start + .05)) });
            }
        }
        return JsonSerializer.Serialize(list);
    }
}
