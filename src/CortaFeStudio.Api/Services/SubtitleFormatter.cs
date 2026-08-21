using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class SubtitleFormatter
{
    private static readonly HashSet<string> EmphasisWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "deus", "jesus", "fe", "graca", "amor", "verdade", "proposito", "promessa",
        "perdao", "paz", "medo", "coragem", "milagre", "reino", "cruz", "espirito",
        "nunca", "sempre", "precisa", "cuidado", "atencao", "impossivel", "possivel",
        "hoje", "agora", "porque", "mas"
    };

    public static string Style(ClipCandidate clip, int width, int height)
    {
        var marginX = width >= 1600 ? 210 : 135;
        var marginV = height switch
        {
            >= 1800 => 430,
            >= 1300 => 310,
            >= 1000 => 220,
            _ => 145
        };
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
        var maxWordsPerLine = width >= 1600 ? 5 : clip.SubtitleStyle == "bold" ? 3 : 4;
        var maxCharactersPerLine = width >= 1600 ? 42 : clip.SubtitleStyle == "bold" ? 19 : 23;
        var lineLength = 0;
        var wordsOnLine = 0;
        var parts = new List<string>();

        foreach (var word in words)
        {
            var text = Escape(word.Word.Trim());
            if (text.Length == 0) continue;

            var needsBreak = wordsOnLine > 0 &&
                (wordsOnLine >= maxWordsPerLine || lineLength + 1 + text.Length > maxCharactersPerLine);
            var separator = needsBreak ? "\\N" : wordsOnLine > 0 ? " " : "";
            if (needsBreak)
            {
                lineLength = 0;
                wordsOnLine = 0;
            }

            var duration = Math.Max(1, (int)Math.Round((word.End - word.Start) * 100));
            var emphasis = IsEmphasisWord(text);
            var token = emphasis
                ? $"{{\\kf{duration}\\b1\\1c&H0000B7FF&}}{text}{{\\rImpacto}}"
                : $"{{\\kf{duration}}}{text}";

            parts.Add(separator + token);
            lineLength += (lineLength > 0 ? 1 : 0) + text.Length;
            wordsOnLine++;
        }

        return string.Concat(parts);
    }

    public static string Plain(string text, int width)
    {
        var limit = width >= 1600 ? 42 : 23;
        var maxWordsPerLine = width >= 1600 ? 6 : 4;
        var length = 0;
        var wordsOnLine = 0;
        var parts = new List<string>();

        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = Escape(raw);
            var needsBreak = wordsOnLine > 0 &&
                (wordsOnLine >= maxWordsPerLine || length + 1 + word.Length > limit);
            var separator = needsBreak ? "\\N" : wordsOnLine > 0 ? " " : "";
            if (needsBreak)
            {
                length = 0;
                wordsOnLine = 0;
            }

            parts.Add(separator + word);
            length += (length > 0 ? 1 : 0) + word.Length;
            wordsOnLine++;
        }

        return string.Concat(parts);
    }

    public static bool IsEmphasisWord(string value)
    {
        var folded = Fold(value)
            .Trim(' ', ',', '.', '?', '!', ':', ';', '-', '—', '"', '\'', '(', ')');
        return folded.Length >= 3 && EmphasisWords.Contains(folded);
    }

    private static string Escape(string value) =>
        value.Replace("\n", " ").Replace("{", "(").Replace("}", ")");

    private static string Fold(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
