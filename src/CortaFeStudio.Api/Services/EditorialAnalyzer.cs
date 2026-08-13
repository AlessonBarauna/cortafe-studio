using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialAnalyzer
{
    private static readonly string[] TransitionOpenings = ["agora vamos", "vamos continuar", "continuando", "próxima característica", "antes de começar", "bom dia", "boa noite", "quem está entendendo", "quem tá entendendo", "beleza gente", "então gente", "como eu estava dizendo"];
    private static readonly string[] IncompleteOpenings = ["e aí", "e então", "porque", "por isso", "mas aí", "ele também", "ela também", "isso também", "como também"];
    private static readonly string[] GenericOpenings = ["beleza", "amém", "entendendo, gente", "quem tá entendendo", "quem está entendendo", "topa", "tá", "né"];
    private static readonly string[] StrongHooks = ["presta atenção", "deixa eu te", "posso te falar", "eu vou repetir", "vou falar mais uma vez", "você tem noção", "imagina isso", "sabe por quê", "o que significa", "o que que", "quando você", "a verdade é", "o problema é"];
    private static readonly string[] Conclusions = ["por isso", "então", "ou seja", "é por isso", "no fim", "a verdade é", "isso significa", "portanto"];
    private static readonly string[] Spiritual = ["deus", "jesus", "fé", "graça", "coração", "reino", "justiça", "perdão", "amor", "cruz", "palavra", "espírito"];
    private static readonly string[] Podcast = ["eu percebi", "na prática", "por exemplo", "o problema", "a verdade", "experiência", "aprendi", "discordo", "ninguém fala"];
    private static readonly string[] Teaching = ["significa", "primeiro", "segundo", "exemplo", "conceito", "definição", "entenda", "observe", "passo", "porque"];

    public List<ClipCandidate> Analyze(List<TranscriptSegment> source, ProjectOptions options)
    {
        var segments = Normalize(source);
        if (options.ContentType == "louvor") return AnalyzeWorship(segments, options);
        var pool = new List<ClipCandidate>();
        for (var anchor = 0; anchor < segments.Count; anchor++)
        {
            var opening = Clean(segments[anchor].Text); if (opening.Length < 8) continue;
            var startIndex = FindNaturalStart(segments, anchor);
            var parts = BuildWindow(segments, startIndex, options);
            if (parts.Count < 3) continue;
            var duration = parts[^1].End - parts[0].Start;
            if (duration < options.MinDuration || duration > options.MaxDuration + 3) continue;
            var text = string.Join(" ", parts.Select(s => Clean(s.Text))).Trim();
            var clip = Score(parts, text, options);
            if (clip.Score >= 45) pool.Add(clip);
        }
        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            var focused = pool.Where(c => c.Reasons.Any(r => r.StartsWith("relacionado ao tema"))).ToList();
            if (focused.Count > 0) pool = focused;
        }
        var targetPool = Math.Clamp(options.ClipCount * 4, options.ClipCount, 40);
        var diverse = new List<ClipCandidate>();
        foreach (var clip in pool.OrderByDescending(c => c.Score).Take(targetPool * 3))
        {
            if (diverse.Any(c => Overlap(c, clip) > .24 || Similar(c.Transcript, clip.Transcript) > .72)) continue;
            diverse.Add(clip); if (diverse.Count >= targetPool) break;
        }
        return diverse.Take(options.ClipCount).OrderBy(c => c.Start).ToList();
    }

    private static ClipCandidate Score(List<TranscriptSegment> parts, string text, ProjectOptions options)
    {
        var lower = text.ToLowerInvariant(); var opening = Clean(parts[0].Text).ToLowerInvariant(); var ending = Clean(parts[^1].Text);
        double score = 38; var reasons = new List<string>();
        var hooks = StrongHooks.Count(opening.Contains); if (hooks > 0) { score += Math.Min(22, hooks * 7); reasons.Add("gancho direto ao público"); }
        var meaningfulQuestion = opening.Contains('?') && opening.Length >= 18 && !GenericOpenings.Any(opening.StartsWith);
        if (meaningfulQuestion || StrongHooks.Any(opening.Contains)) { score += 11; reasons.Add("abertura forte"); }
        if (TransitionOpenings.Any(opening.StartsWith)) { score -= 32; reasons.Add("penalizado: fala de transição"); }
        if (IncompleteOpenings.Any(opening.StartsWith)) { score -= 18; reasons.Add("penalizado: início dependente"); }
        if (GenericOpenings.Any(opening.StartsWith)) { score -= 25; reasons.Add("penalizado: abertura genérica"); }
        if (EndsThought(ending)) { score += 10; reasons.Add("conclusão completa"); } else score -= 10;
        var contrasts = new[] { " mas ", " porém ", " não é ", " mesmo em ", " ao contrário", " enquanto " }.Count(lower.Contains);
        if (contrasts > 0) { score += Math.Min(16, contrasts * 5); reasons.Add("contraste memorável"); }
        var conclusion = Conclusions.Count(lower.Contains); if (conclusion > 0) { score += Math.Min(12, conclusion * 4); reasons.Add("desenvolve uma conclusão"); }
        var profileWords = options.ContentType switch { "podcast" => Podcast, "aula" => Teaching, _ => Spiritual };
        var profileHits = profileWords.Count(lower.Contains); if (profileHits > 1) { score += Math.Min(15, profileHits * 2.5); reasons.Add($"relevante para {ProfileLabel(options.ContentType)}"); }
        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            var foldedText = Fold(text); var terms = Tokenize(Fold(options.Topic)).Where(t => t.Length > 2).ToArray(); var hits = terms.Count(foldedText.Contains);
            if (hits > 0) { score += Math.Min(28, hits * 10); reasons.Add($"relacionado ao tema “{options.Topic}”"); } else score -= 12;
        }
        var wordCount = Tokenize(text).Count; if (wordCount is >= 65 and <= 190) score += 7; else if (wordCount < 35) score -= 18;
        var title = MakeTitle(text, options.ContentType);
        return new ClipCandidate { Start = parts[0].Start, End = parts[^1].End, Score = Math.Round(Math.Clamp(score, 0, 99), 1), Transcript = text, Title = title, CoverText = string.Join(' ', title.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6)), Caption = $"{title}. Uma mensagem para guardar e compartilhar. ✨", EditorialProfile = options.ContentType, Reasons = reasons.Distinct().Take(4).ToList() };
    }

    private static List<TranscriptSegment> BuildWindow(List<TranscriptSegment> segments, int startIndex, ProjectOptions options)
    {
        var parts = new List<TranscriptSegment>(); var start = segments[startIndex].Start;
        for (var i = startIndex; i < segments.Count; i++)
        {
            if (segments[i].End - start > options.MaxDuration + 3) break;
            parts.Add(segments[i]); var elapsed = parts[^1].End - start;
            if (elapsed >= options.MinDuration && EndsThought(parts[^1].Text) && HasResolution(parts)) break;
        }
        return parts;
    }
    private static int FindNaturalStart(List<TranscriptSegment> segments, int anchor)
    {
        var text = Clean(segments[anchor].Text).ToLowerInvariant();
        return anchor;
    }
    private static bool HasResolution(List<TranscriptSegment> parts) { var last = string.Join(" ", parts.TakeLast(Math.Min(3, parts.Count)).Select(x => x.Text)).ToLowerInvariant(); return Conclusions.Any(last.Contains) || EndsThought(parts[^1].Text); }
    private static List<ClipCandidate> AnalyzeWorship(List<TranscriptSegment> segments, ProjectOptions options)
    {
        var usable = segments.Where(s => Clean(s.Text).Replace("[música]", "", StringComparison.OrdinalIgnoreCase).Length > 2).ToList(); var pool = new List<ClipCandidate>();
        for (var i = 0; i < usable.Count; i += 2)
        {
            var parts = usable.Skip(i).TakeWhile(s => s.End - usable[i].Start <= options.MaxDuration).ToList(); if (parts.Count < 3 || parts[^1].End - parts[0].Start < options.MinDuration) continue;
            var text = string.Join(" ", parts.Select(s => Clean(s.Text).Replace("[música]", "", StringComparison.OrdinalIgnoreCase))); var repeated = Tokenize(text).GroupBy(x => x).Count(g => g.Count() >= 3); double score = 55 + Math.Min(22, repeated * 3) + Spiritual.Count(text.ToLowerInvariant().Contains) * 2;
            pool.Add(new ClipCandidate { Start = parts[0].Start, End = parts[^1].End, Score = Math.Round(Math.Min(96, score), 1), Transcript = text, Title = "Momento de louvor e adoração", CoverText = "UM MOMENTO DE ADORAÇÃO", Caption = "Uma canção para renovar a fé. 🎶✨", EditorialProfile = "louvor", Reasons = ["trecho lírico contínuo", repeated > 0 ? "possível refrão ou repetição" : "boa densidade de letra"] });
        }
        return SelectDiverse(pool, options.ClipCount);
    }
    private static List<ClipCandidate> SelectDiverse(List<ClipCandidate> pool, int count) { var result = new List<ClipCandidate>(); foreach (var c in pool.OrderByDescending(x => x.Score)) { if (result.Any(x => Overlap(x, c) > .22)) continue; result.Add(c); if (result.Count == count) break; } return result.OrderBy(x => x.Start).ToList(); }
    private static List<TranscriptSegment> Normalize(List<TranscriptSegment> source) => source.Where(s => s.End > s.Start && !string.IsNullOrWhiteSpace(s.Text)).OrderBy(s => s.Start).ToList();
    private static string Clean(string value) => string.Join(' ', value.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static bool EndsThought(string value) => value.TrimEnd().EndsWith('.') || value.TrimEnd().EndsWith('?') || value.TrimEnd().EndsWith('!');
    private static double Overlap(ClipCandidate a, ClipCandidate b) => Math.Max(0, Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start)) / Math.Min(a.End - a.Start, b.End - b.Start);
    private static double Similar(string a, string b) { var x = Tokenize(a).ToHashSet(); var y = Tokenize(b).ToHashSet(); return x.Count == 0 || y.Count == 0 ? 0 : x.Intersect(y).Count() / (double)x.Union(y).Count(); }
    private static List<string> Tokenize(string value) => value.ToLower(CultureInfo.GetCultureInfo("pt-BR")).Split([' ', ',', '.', '?', '!', ':', ';', '—', '-'], StringSplitOptions.RemoveEmptyEntries).ToList();
    private static string Fold(string value) { var normalized = value.Normalize(NormalizationForm.FormD); var sb = new StringBuilder(); foreach (var c in normalized) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c)); return sb.ToString().Normalize(NormalizationForm.FormC); }
    private static string ProfileLabel(string profile) => profile switch { "podcast" => "podcast", "aula" => "conteúdo educativo", _ => "pregação" };
    private static string MakeTitle(string text, string profile) { var lower = text.ToLowerInvariant(); if (lower.Contains("coração limpo")) return "O que realmente limpa o coração?"; if (lower.Contains("tranquilidade") && lower.Contains("paz")) return "Paz não é o mesmo que tranquilidade"; if (lower.Contains("perdoa") && lower.Contains("jesus")) return "O coração de Jesus na cruz"; if (lower.Contains("desconfia") && lower.Contains("deus")) return "A desconfiança que suja o coração"; var sentences = text.Split(['.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 24).ToList(); var sentence = sentences.FirstOrDefault(x => StrongHooks.Any(x.ToLowerInvariant().Contains)) ?? sentences.FirstOrDefault() ?? text; return string.Join(' ', sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(9)).Trim(',', '.', '?', '!'); }
}
