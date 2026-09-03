using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed record EditorialMoment(double Start, double End, string Kind, double Strength, string Label);

/// <summary>
/// Detecta momentos editoriais dentro de um corte sem depender de API externa.
/// Usa timestamps reais quando disponíveis e cai para uma distribuição aproximada
/// quando só existe o texto consolidado do corte.
/// </summary>
public static class EditorialMomentDetector
{
    private static readonly string[] HookTerms =
    [
        "presta atenção", "olha isso", "sabe por que", "você precisa", "voce precisa",
        "deixa eu te falar", "a verdade é", "a verdade e", "escuta isso", "imagina"
    ];

    private static readonly string[] ScriptureTerms =
    [
        "bíblia", "biblia", "palavra diz", "está escrito", "esta escrito", "versículo", "versiculo",
        "mateus", "marcos", "lucas", "joão", "joao", "romanos", "coríntios", "corintios",
        "salmos", "provérbios", "proverbios", "isaías", "isaias", "gênesis", "genesis",
        "apocalipse", "efésios", "efesios", "filipenses", "hebreus"
    ];

    private static readonly string[] ClimaxTerms =
    [
        "mas deus", "então entenda", "entao entenda", "é por isso", "e por isso",
        "nunca esqueça", "nunca esqueca", "você não pode", "voce nao pode", "milagre",
        "promessa", "propósito", "proposito", "deus vai", "jesus fez", "a cruz"
    ];

    private static readonly string[] ConclusionTerms =
    [
        "por isso", "então hoje", "entao hoje", "daqui pra frente", "guarda isso",
        "lembre disso", "fica com isso", "essa é a mensagem", "essa e a mensagem",
        "amém", "amen"
    ];

    public static IReadOnlyList<EditorialMoment> Detect(IReadOnlyList<TranscriptWord> source, double duration)
    {
        var words = source.Where(word => !string.IsNullOrWhiteSpace(word.Word)).OrderBy(word => word.Start).ToList();
        if (words.Count == 0 || duration <= 0) return [];

        var candidates = new List<EditorialMoment>();
        for (var index = 0; index < words.Count; index++)
        {
            var left = Math.Max(0, index - 3);
            var count = Math.Min(8, words.Count - left);
            var window = string.Join(' ', words.Skip(left).Take(count).Select(word => word.Word));
            AddFromText(candidates, window, words[index].Start, duration);
        }

        if (words.Count >= 5)
        {
            var opening = string.Join(' ', words.Take(Math.Min(12, words.Count)).Select(word => word.Word));
            if (HookScore(opening) >= 5)
                candidates.Add(new EditorialMoment(Math.Max(.15, words[0].Start), Math.Min(duration, words[Math.Min(words.Count - 1, 8)].End + .18), "hook", .88, "Gancho"));
        }

        return Consolidate(candidates, duration);
    }

    public static IReadOnlyList<EditorialMoment> Detect(string transcript, double duration)
    {
        if (string.IsNullOrWhiteSpace(transcript) || duration <= 0) return [];
        var tokens = transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return [];
        var secondsPerToken = duration / Math.Max(1, tokens.Length);
        var words = tokens.Select((token, index) => new TranscriptWord
        {
            Start = index * secondsPerToken,
            End = Math.Min(duration, (index + 1) * secondsPerToken),
            Word = token
        }).ToList();
        return Detect(words, duration);
    }

    public static string KindAt(IReadOnlyList<TranscriptWord> words, double duration, double time)
    {
        var moment = Detect(words, duration)
            .Where(item => time >= item.Start - .08 && time <= item.End + .08)
            .OrderByDescending(item => item.Strength)
            .FirstOrDefault();
        return moment?.Kind ?? "";
    }

    private static void AddFromText(List<EditorialMoment> target, string text, double at, double duration)
    {
        var folded = Fold(text);
        var hook = HookScore(folded);
        var scripture = ScriptureTerms.Count(folded.Contains);
        var climax = ClimaxTerms.Count(folded.Contains);
        var conclusion = ConclusionTerms.Count(folded.Contains);
        var punctuation = text.Contains('!') ? 1 : text.Contains('?') ? .7 : 0;

        if (hook >= 5 && at <= Math.Max(12, duration * .24))
            target.Add(Moment(at, duration, "hook", Math.Min(1, .68 + hook * .025), "Gancho"));
        if (scripture > 0)
            target.Add(Moment(at, duration, "scripture", Math.Min(.96, .73 + scripture * .08), "Versículo / referência"));
        if (climax > 0 || punctuation > 0 && at >= duration * .2)
            target.Add(Moment(at, duration, "climax", Math.Min(1, .66 + climax * .12 + punctuation * .12), "Clímax"));
        if (conclusion > 0 && at >= duration * .58)
            target.Add(Moment(at, duration, "conclusion", Math.Min(.94, .68 + conclusion * .1), "Conclusão"));
    }

    private static EditorialMoment Moment(double at, double duration, string kind, double strength, string label)
    {
        var start = Math.Clamp(at - .18, .12, Math.Max(.12, duration - .8));
        var length = kind switch { "scripture" => 2.6, "hook" => 1.7, "conclusion" => 2.0, _ => 1.45 };
        return new EditorialMoment(Math.Round(start, 3), Math.Round(Math.Min(duration - .08, start + length), 3), kind, Math.Round(strength, 3), label);
    }

    private static IReadOnlyList<EditorialMoment> Consolidate(IEnumerable<EditorialMoment> source, double duration)
    {
        var ordered = source.Where(item => item.End > item.Start && item.Start < duration)
            .OrderByDescending(item => item.Strength)
            .ThenBy(item => item.Start)
            .ToList();
        var selected = new List<EditorialMoment>();
        foreach (var candidate in ordered)
        {
            if (selected.Any(existing => existing.Kind == candidate.Kind && Math.Abs(existing.Start - candidate.Start) < 5)) continue;
            if (selected.Any(existing => Math.Abs(existing.Start - candidate.Start) < 1.2 && existing.Strength >= candidate.Strength)) continue;
            selected.Add(candidate);
            if (selected.Count >= 7) break;
        }
        return selected.OrderBy(item => item.Start).ToList();
    }

    private static int HookScore(string value)
    {
        var folded = Fold(value);
        var score = HookTerms.Count(folded.Contains) * 5;
        if (value.Contains('?')) score += 3;
        if (folded.StartsWith("voce ") || folded.StartsWith("nao ")) score += 2;
        return score;
    }

    private static string Fold(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
