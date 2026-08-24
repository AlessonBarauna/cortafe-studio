using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialScoringService
{
    private static readonly string[] TransitionOpenings =
    [
        "agora vamos",
        "vamos continuar",
        "continuando",
        "próxima característica",
        "antes de começar",
        "bom dia",
        "boa noite",
        "quem está entendendo",
        "quem tá entendendo",
        "beleza gente",
        "então gente",
        "como eu estava dizendo"
    ];

    private static readonly string[] IncompleteOpenings =
    [
        "e aí",
        "e então",
        "porque",
        "por isso",
        "mas aí",
        "ele também",
        "ela também",
        "isso também",
        "como também"
    ];

    private static readonly string[] GenericOpenings =
    [
        "beleza",
        "amém",
        "entendendo, gente",
        "quem tá entendendo",
        "quem está entendendo",
        "topa",
        "tá",
        "né"
    ];

    private static readonly string[] StrongHooks =
    [
        "presta atenção",
        "deixa eu te",
        "posso te falar",
        "eu vou repetir",
        "vou falar mais uma vez",
        "você tem noção",
        "imagina isso",
        "sabe por quê",
        "o que significa",
        "o que que",
        "quando você",
        "a verdade é",
        "o problema é"
    ];

    private static readonly string[] Conclusions =
    [
        "por isso",
        "então",
        "ou seja",
        "é por isso",
        "no fim",
        "a verdade é",
        "isso significa",
        "portanto"
    ];

    private static readonly string[] ContextDependence =
    [
        "como eu disse",
        "isso aqui",
        "aquilo",
        "esse ponto",
        "continuando",
        "voltando",
        "o mesmo",
        "essa parte"
    ];

    private static readonly string[] StorySignals =
    [
        "um dia",
        "aconteceu",
        "eu lembro",
        "certa vez",
        "quando eu",
        "naquele momento"
    ];

    private static readonly string[] ImpactSignals =
    [
        "mudou minha vida", "nunca mais", "nesse momento", "foi então", "o segredo",
        "ninguém te contou", "você precisa", "não desista", "impossível", "milagre",
        "decisão", "propósito", "dor", "perda", "cura", "transformação", "recomeço"
    ];

    private static readonly string[] ActionSignals =
    [
        "faça", "pare", "comece", "lembre", "entenda", "escute", "olhe", "pense",
        "pergunte", "decida", "confie", "acredite"
    ];

    private static readonly string[] FillerSignals =
    [
        "tipo assim", "vamos lá", "é isso", "entendeu", "tá bom", "basicamente",
        "de certa forma", "digamos assim"
    ];

    public ClipCandidate Score(
        List<TranscriptSegment> parts,
        string text,
        ProjectOptions options)
    {
        var lower = text.ToLowerInvariant();
        var opening = Clean(parts[0].Text).ToLowerInvariant();
        var ending = Clean(parts[^1].Text);

        var reasons = new List<string>();
        var breakdown = new EditorialScoreBreakdown();

        var hooks = StrongHooks.Count(opening.Contains);

        if (hooks > 0)
        {
            breakdown.Hook += Math.Min(22, hooks * 7);
            reasons.Add("gancho direto ao público");
        }

        var meaningfulQuestion =
            opening.Contains('?') &&
            opening.Length >= 18 &&
            !GenericOpenings.Any(opening.StartsWith);

        if (meaningfulQuestion || StrongHooks.Any(opening.Contains))
        {
            breakdown.OpeningAdjustment += 11;
            reasons.Add("abertura forte");
        }

        if (TransitionOpenings.Any(opening.StartsWith))
        {
            breakdown.OpeningAdjustment -= 32;
            reasons.Add("penalizado: fala de transição");
        }

        if (IncompleteOpenings.Any(opening.StartsWith))
        {
            breakdown.OpeningAdjustment -= 18;
            reasons.Add("penalizado: início dependente");
        }

        if (GenericOpenings.Any(opening.StartsWith))
        {
            breakdown.OpeningAdjustment -= 25;
            reasons.Add("penalizado: abertura genérica");
        }

        if (EndsThought(ending))
        {
            breakdown.Completion += 10;
            reasons.Add("conclusão completa");
        }
        else
        {
            breakdown.Completion -= 10;
        }

        var contrasts = new[]
        {
            " mas ",
            " porém ",
            " não é ",
            " mesmo em ",
            " ao contrário",
            " enquanto "
        }.Count(lower.Contains);

        if (contrasts > 0)
        {
            breakdown.Contrast += Math.Min(16, contrasts * 5);
            reasons.Add("contraste memorável");
        }

        var conclusion = Conclusions.Count(lower.Contains);

        if (conclusion > 0)
        {
            breakdown.Conclusion += Math.Min(12, conclusion * 4);
            reasons.Add("desenvolve uma conclusão");
        }

        breakdown.Structure =
            StructureScore(parts, lower, out var structureReason);

        if (structureReason is not null)
            reasons.Insert(0, structureReason);

        if (ContextDependence.Any(opening.Contains))
        {
            breakdown.ContextPenalty -= 18;
            reasons.Add("penalizado: depende do contexto anterior");
        }

        var profile = EditorialProfiles.Get(options.ContentType);

        var profileHits = profile.Signals.Count(lower.Contains);

        if (profileHits > 1)
        {
            breakdown.ProfileRelevance +=
                Math.Min(15, profileHits * 2.5);

            reasons.Add(
                $"relevante para {ProfileLabel(options.ContentType)}");
        }

        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            var foldedText = Fold(text);

            var terms = Tokenize(Fold(options.Topic))
                .Where(term => term.Length > 2)
                .ToArray();

            var hits = terms.Count(foldedText.Contains);

            if (hits > 0)
            {
                breakdown.TopicRelevance +=
                    Math.Min(28, hits * 10);

                reasons.Add(
                    $"relacionado ao tema “{options.Topic}”");
            }
            else
            {
                breakdown.TopicRelevance -= 12;
            }
        }

        var impactHits = ImpactSignals.Count(lower.Contains);
        var actionHits = ActionSignals.Count(lower.Contains);
        var directAddress = new[] { " você ", " seu ", " sua ", " contigo ", " te " }.Count(lower.Contains);
        var quotableContrast = lower.Contains(" não ") && (lower.Contains(" mas ") || lower.Contains(" e sim "));
        breakdown.Impact = Math.Min(14, impactHits * 3.5 + actionHits * 2 + Math.Min(4, directAddress) + (quotableContrast ? 4 : 0));
        if (breakdown.Impact >= 8) reasons.Insert(0, "alto potencial de impacto e compartilhamento");
        else if (breakdown.Impact >= 4) reasons.Insert(0, "fala direta e memorável");

        var sentences = text.Count(character => character is '.' or '?' or '!');
        var fillerHits = FillerSignals.Count(lower.Contains);
        var duration = Math.Max(1, parts[^1].End - parts[0].Start);
        var wordsPerMinute = Tokenize(text).Count / duration * 60;
        breakdown.Clarity = sentences >= 2 ? 4 : 0;
        if (wordsPerMinute is >= 85 and <= 210) breakdown.Clarity += 4;
        if (fillerHits > 0) breakdown.Clarity -= Math.Min(8, fillerHits * 3);
        if (breakdown.Clarity >= 6) reasons.Insert(Math.Min(1, reasons.Count), "ritmo claro e boa densidade de ideias");
        else if (breakdown.Clarity < 0) reasons.Add("penalizado: excesso de fala de apoio");

        var wordCount = Tokenize(text).Count;

        if (wordCount is >= 65 and <= 190)
        {
            breakdown.LengthAdjustment += 7;
        }
        else if (wordCount < 35)
        {
            breakdown.LengthAdjustment -= 18;
        }

        var title = MakeTitle(text);

        return new ClipCandidate
        {
            Start = parts[0].Start,
            End = parts[^1].End,
            Score = breakdown.Total,
            ScoreBreakdown = breakdown,
            HookSentence = Clean(parts[0].Text),
            Transcript = text,
            Title = title,
            CoverText = string.Join(
                ' ',
                title
                    .ToUpperInvariant()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Take(6)),
            Caption = $"{title}. {profile.CaptionSuffix} ✨",
            Hashtags = profile.Hashtags.ToList(),
            EditorialProfile = options.ContentType,
            Reasons = reasons
                .Distinct()
                .Take(8)
                .ToList()
        };
    }

    public bool IsTransitionOpening(string text)
    {
        var opening = Clean(text).ToLowerInvariant();
        return TransitionOpenings.Any(opening.StartsWith);
    }

    public bool IsIncompleteOpening(string text)
    {
        var opening = Clean(text).ToLowerInvariant();
        return IncompleteOpenings.Any(opening.StartsWith);
    }

    public bool DependsOnContext(string text)
    {
        var opening = Clean(text).ToLowerInvariant();
        return ContextDependence.Any(opening.Contains);
    }

    public bool HasResolution(List<TranscriptSegment> parts)
    {
        var last = string.Join(
                " ",
                parts
                    .TakeLast(Math.Min(3, parts.Count))
                    .Select(part => part.Text))
            .ToLowerInvariant();

        return Conclusions.Any(last.Contains) ||
               last.Contains("assim, ") ||
               last.Contains("desse modo");
    }

    private static double StructureScore(
        List<TranscriptSegment> parts,
        string lower,
        out string? reason)
    {
        reason = null;

        var opening =
            Clean(parts[0].Text).ToLowerInvariant();

        var ending =
            Clean(parts[^1].Text).ToLowerInvariant();

        var hasSetup =
            opening.Contains('?') ||
            StrongHooks.Any(opening.Contains) ||
            StorySignals.Any(opening.Contains);

        var hasDevelopment =
            lower.Contains(" porque ") ||
            lower.Contains(" mas ") ||
            lower.Contains(" por exemplo") ||
            parts.Count >= 6;

        var hasResolution =
            EndsThought(ending) &&
            (Conclusions.Any(ending.Contains) ||
             Conclusions.Any(lower.Contains));

        var phases =
            (hasSetup ? 1 : 0) +
            (hasDevelopment ? 1 : 0) +
            (hasResolution ? 1 : 0);

        if (phases == 3)
        {
            reason =
                "ideia completa: gancho, desenvolvimento e conclusão";

            return 14;
        }

        if (phases == 2)
        {
            reason = "ideia bem desenvolvida";
            return 6;
        }

        return -7;
    }

    private static string MakeTitle(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("coração limpo"))
            return "O que realmente limpa o coração?";

        if (lower.Contains("tranquilidade") &&
            lower.Contains("paz"))
            return "Paz não é o mesmo que tranquilidade";

        if (lower.Contains("perdoa") &&
            lower.Contains("jesus"))
            return "O coração de Jesus na cruz";

        if (lower.Contains("desconfia") &&
            lower.Contains("deus"))
            return "A desconfiança que suja o coração";

        var sentences = text
            .Split(
                ['.', '?', '!'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 24)
            .ToList();

        var sentence =
            sentences.FirstOrDefault(candidate =>
                StrongHooks.Any(
                    candidate
                        .ToLowerInvariant()
                        .Contains))
            ?? sentences.FirstOrDefault()
            ?? text;

        return string.Join(
                ' ',
                sentence
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Take(9))
            .Trim(',', '.', '?', '!');
    }

    private static string Clean(string value) =>
        string.Join(
            ' ',
            value
                .Replace("\n", " ")
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries));

    private static bool EndsThought(string value) =>
        value.TrimEnd().EndsWith('.') ||
        value.TrimEnd().EndsWith('?') ||
        value.TrimEnd().EndsWith('!');

    private static List<string> Tokenize(string value) =>
        value
            .ToLower(
                CultureInfo.GetCultureInfo("pt-BR"))
            .Split(
                [' ', ',', '.', '?', '!', ':', ';', '—', '-'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static string Fold(string value)
    {
        var normalized =
            value.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(
                    char.ToLowerInvariant(character));
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    private static string ProfileLabel(string profile) =>
        EditorialProfiles
            .Get(profile)
            .Label
            .ToLowerInvariant();
}
