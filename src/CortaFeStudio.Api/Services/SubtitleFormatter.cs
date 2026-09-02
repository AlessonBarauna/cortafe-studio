using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class SubtitleFormatter
{
    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "e", "o", "as", "os", "de", "da", "do", "das", "dos", "em", "no", "na", "nos", "nas",
        "que", "se", "com", "por", "para", "um", "uma", "mas", "ou"
    };
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
            >= 1800 => 235,
            >= 1300 => 175,
            >= 1000 => 145,
            _ => 100
        };
        var (font, size, primary, secondary, outline, shadow) = clip.SubtitleStyle switch
        {
            "clean" => ("Arial", 40, "&H00FFFFFF", "&H00FFFFFF", 2, 0),
            "podcast" => ("Arial", 43, "&H00FFFFFF", "&H00F0B44D", 3, 1),
            "sermon" => ("Arial Black", 46, "&H00FFFFFF", "&H0000B7FF", 3, 1),
            "motivational" => ("Arial Black", 47, "&H00FFFFFF", "&H0048D7FF", 3, 1),
            "minimal" => ("Arial", 38, "&H00FFFFFF", "&H00FFFFFF", 2, 0),
            "worship" => ("Georgia", 42, "&H00FFFFFF", "&H00E8C58B", 3, 1),
            "bold" => ("Arial Black", 49, "&H00FFFFFF", "&H0000B7FF", 4, 1),
            _ => ("Arial", 45, "&H00FFFFFF", "&H0000B7FF", 3, 1)
        };
        return $"Style: Impacto,{font},{size},{primary},{secondary},&H00120B22,&H80000000,-1,0,0,0,100,100,0,0,1,{outline},{shadow},2,{marginX},{marginX},{marginV},1";
    }

    public static string Position(SubtitleTrack track, int width, int height)
    {
        var x = Math.Round(width * Math.Clamp(track.PositionX, 5, 95) / 100d);
        var y = Math.Round(height * Math.Clamp(track.PositionY, 5, 95) / 100d);
        return $"{{\\pos({x:0},{y:0})}}";
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

        var animation = clip.SubtitleStyle == "minimal" ? "{\\fad(70,90)}" : "{\\fad(55,75)\\fscx98\\fscy98\\t(0,100,\\fscx100\\fscy100)}";
        return animation + string.Concat(parts);
    }

    public static IReadOnlyList<IReadOnlyList<TranscriptWord>> SemanticUnits(IReadOnlyList<TranscriptWord> source)
    {
        var words = source.Where(word => !string.IsNullOrWhiteSpace(word.Word)).OrderBy(word => word.Start).ToList();
        var units = new List<List<TranscriptWord>>();
        for (var index = 0; index < words.Count;)
        {
            var unit = new List<TranscriptWord>();
            while (index < words.Count && unit.Count < 5)
            {
                var word = words[index++]; unit.Add(word);
                var pause = index < words.Count ? words[index].Start - word.End : 0;
                var punctuation = word.Word.TrimEnd().EndsWithAny('.', '!', '?', ':', ';');
                if (unit.Count >= 2 && (pause >= .42 || punctuation)) break;
                if (unit.Count >= 4 && index < words.Count && !IsConnector(words[index].Word)) break;
            }
            if (unit.Count == 1 && units.Count > 0 && units[^1].Count < 5 && !IsConnector(unit[0].Word)) units[^1].Add(unit[0]);
            else units.Add(unit);
        }

        for (var index = 0; index < units.Count - 1; index++)
        {
            var current = units[index]; var next = units[index + 1];
            if (current.Count > 2 && next.Count < 5 && IsConnector(current[^1].Word)) { next.Insert(0, current[^1]); current.RemoveAt(current.Count - 1); }
            if (next.Count == 1 && current.Count < 5) { current.Add(next[0]); units.RemoveAt(index + 1); index--; }
        }
        if (units.Count > 1 && units[0].Count == 1 && units[1].Count < 5)
        {
            units[1].Insert(0, units[0][0]); units.RemoveAt(0);
        }
        return units;
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
        if (EmphasisWords.Contains(folded)) return true;
        return folded.Length >= 8 &&
            (folded.EndsWith("dade", StringComparison.Ordinal) || folded.EndsWith("cao", StringComparison.Ordinal) || folded.EndsWith("mente", StringComparison.Ordinal));
    }

    private static bool IsConnector(string value) => Connectors.Contains(Fold(value).Trim(' ', ',', '.', '?', '!', ':', ';', '-', '—', '"', '\'', '(', ')'));

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

file static class SubtitleStringExtensions
{
    public static bool EndsWithAny(this string value, params char[] endings) => value.Length > 0 && endings.Contains(value[^1]);
}
