using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class SubtitleFormatter
{
    public static string Style(ClipCandidate clip, int width, int height)
    {
        var marginX = width >= 1600 ? 190 : 125;
        var marginV = height switch { >= 1800 => 390, >= 1300 => 270, >= 1000 => 190, _ => 125 };
        var (font, size, primary, secondary, outline, shadow) = clip.SubtitleStyle switch
        {
            "clean" => ("Arial", 54, "&H00FFFFFF", "&H00FFFFFF", 3, 0),
            "bold" => ("Arial Black", 66, "&H00FFFFFF", "&H0000B7FF", 6, 2),
            _ => ("Arial", 62, "&H00FFFFFF", "&H0000B7FF", 5, 2)
        };
        return $"Style: Impacto,{font},{size},{primary},{secondary},&H00120B22,&H80000000,-1,0,0,0,100,100,0,0,1,{outline},{shadow},2,{marginX},{marginX},{marginV},1";
    }

    public static string Karaoke(IReadOnlyList<TranscriptWord> words, ClipCandidate clip, int width)
    {
        var limit = width >= 1600 ? 44 : clip.SubtitleStyle == "bold" ? 20 : 24;
        var lineLength = 0; var parts = new List<string>();
        foreach (var word in words)
        {
            var text = Escape(word.Word.Trim()); if (text.Length == 0) continue;
            var separator = lineLength > 0 && lineLength + 1 + text.Length > limit ? "\\N" : lineLength > 0 ? " " : "";
            lineLength = separator == "\\N" ? text.Length : lineLength + (lineLength > 0 ? 1 : 0) + text.Length;
            parts.Add($"{separator}{{\\kf{Math.Max(1, (int)Math.Round((word.End - word.Start) * 100))}}}{text}");
        }
        return string.Concat(parts);
    }

    public static string Plain(string text, int width)
    {
        var limit = width >= 1600 ? 44 : 24; var length = 0; var parts = new List<string>();
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = Escape(raw); var separator = length > 0 && length + 1 + word.Length > limit ? "\\N" : length > 0 ? " " : "";
            length = separator == "\\N" ? word.Length : length + (length > 0 ? 1 : 0) + word.Length; parts.Add(separator + word);
        }
        return string.Concat(parts);
    }

    private static string Escape(string value) => value.Replace("\n", " ").Replace("{", "(").Replace("}", ")");
}
