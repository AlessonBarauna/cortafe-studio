using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public interface IEditorialAiProvider
{
    string Name { get; }
    bool IsAvailable();
    EditorialIntelligenceResult Analyze(IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options);
    List<SemanticClipEvaluation> Evaluate(IReadOnlyList<ClipCandidate> clips, ProjectOptions options, IReadOnlyList<EditorialTopic> topics);
    List<EditorialSeries> Cluster(IReadOnlyList<ClipCandidate> clips, IReadOnlyList<SemanticClipEvaluation> evaluations, ProjectOptions options);
}

public static class EditorialAiProviderFactory
{
    private static readonly Lazy<IEditorialAiProvider> Ollama = new(() => new OllamaEditorialAiProvider());
    private static readonly IEditorialAiProvider Heuristic = new HeuristicEditorialAiProvider();

    public static IEditorialAiProvider CreateDefault() => Ollama.Value.IsAvailable() ? Ollama.Value : Heuristic;
}

internal sealed class OllamaEditorialAiProvider : IEditorialAiProvider
{
    private const string Endpoint = "http://localhost:11434/api/generate";
    private const string Model = "qwen2.5:3b";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(40) };
    public string Name => "ollama-qwen2.5:3b";

    public bool IsAvailable()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:11434/api/tags");
            using var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public EditorialIntelligenceResult Analyze(IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options)
    {
        var fallback = new HeuristicEditorialAiProvider().Analyze(transcript, options);
        if (transcript.Count == 0) return fallback;
        try
        {
            var chunks = TranscriptChunks(transcript, 28 * 60);
            var topics = new List<EditorialTopic>();
            foreach (var chunk in chunks)
            {
                var prompt = $"""
Você é um diretor editorial sênior de vídeos curtos em português do Brasil.
Analise este trecho de {Profile(options.ContentType)} e descubra os assuntos semanticamente completos.
Retorne SOMENTE JSON válido no formato:
{{"mainTheme":"...","summary":"...","topics":[{{"title":"...","summary":"...","start":0.0,"end":90.0,"confidence":0.0,"keywords":["..."]}}]}}
Regras: use segundos absolutos informados entre colchetes; gere de 2 a 7 temas; não invente assunto; cada tema deve ter começo e fim coerentes; prefira assuntos que possam originar cortes independentes.

TRANSCRIÇÃO:
{BuildTimedText(chunk, 12000)}
""";
                var parsed = Generate<EditorialIntelligenceResult>(prompt);
                if (parsed?.Topics.Count > 0) topics.AddRange(parsed.Topics);
                if (!string.IsNullOrWhiteSpace(parsed?.MainTheme) && string.IsNullOrWhiteSpace(fallback.MainTheme)) fallback.MainTheme = parsed.MainTheme;
                if (!string.IsNullOrWhiteSpace(parsed?.Summary) && string.IsNullOrWhiteSpace(fallback.Summary)) fallback.Summary = parsed.Summary;
            }
            var normalized = NormalizeTopics(topics, transcript[^1].End);
            if (normalized.Count > 0) fallback.Topics = normalized;
            fallback.Provider = Name;
            fallback.MainTheme = string.IsNullOrWhiteSpace(fallback.MainTheme) ? fallback.Topics.FirstOrDefault()?.Title ?? Profile(options.ContentType) : fallback.MainTheme;
            return fallback;
        }
        catch { return fallback; }
    }

    public List<SemanticClipEvaluation> Evaluate(IReadOnlyList<ClipCandidate> clips, ProjectOptions options, IReadOnlyList<EditorialTopic> topics)
    {
        if (clips.Count == 0) return [];
        var fallback = new HeuristicEditorialAiProvider().Evaluate(clips, options, topics);
        try
        {
            var input = clips.Select(clip => new
            {
                clip.Id,
                clip.Start,
                clip.End,
                transcript = Truncate(clip.Transcript, 1600),
                title = clip.Title
            });
            var topicMap = topics.Select(t => new { t.Title, t.Summary, t.Start, t.End });
            var prompt = $"""
Você é um editor sênior de Reels, Shorts e TikTok para {Profile(options.ContentType)}.
Avalie cada corte sem alterar os IDs. Considere: gancho, ideia completa, valor emocional, clareza sem contexto anterior, potencial de compartilhamento e fidelidade ao conteúdo.
Retorne SOMENTE um array JSON:
[{{"clipId":"id","score":0.0,"reason":"motivo curto","topic":"tema","shareability":0.0,"emotionalValue":0.0,"standaloneClarity":0.0}}]
Todos os números devem estar entre 0 e 100. Evite elogios genéricos.
Mapa de temas: {JsonSerializer.Serialize(topicMap)}
Cortes: {JsonSerializer.Serialize(input)}
""";
            var generated = Generate<List<SemanticClipEvaluation>>(prompt);
            if (generated is null || generated.Count == 0) return fallback;
            var byId = generated
                .Where(x => !string.IsNullOrWhiteSpace(x.ClipId))
                .GroupBy(x => x.ClipId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var item in fallback)
            {
                if (!byId.TryGetValue(item.ClipId, out var ai)) continue;
                item.Score = Clamp(ai.Score);
                item.Shareability = Clamp(ai.Shareability);
                item.EmotionalValue = Clamp(ai.EmotionalValue);
                item.StandaloneClarity = Clamp(ai.StandaloneClarity);
                item.Reason = string.IsNullOrWhiteSpace(ai.Reason) ? item.Reason : ai.Reason.Trim();
                item.Topic = string.IsNullOrWhiteSpace(ai.Topic) ? item.Topic : ai.Topic.Trim();
            }
            return fallback;
        }
        catch { return fallback; }
    }

    public List<EditorialSeries> Cluster(IReadOnlyList<ClipCandidate> clips, IReadOnlyList<SemanticClipEvaluation> evaluations, ProjectOptions options)
    {
        var fallback = new HeuristicEditorialAiProvider().Cluster(clips, evaluations, options);
        if (clips.Count < 2) return fallback;
        try
        {
            var payload = clips.Select(c => new
            {
                c.Id,
                c.Title,
                transcript = Truncate(c.Transcript, 500),
                semanticTopic = evaluations.FirstOrDefault(e => e.ClipId == c.Id)?.Topic,
                score = evaluations.FirstOrDefault(e => e.ClipId == c.Id)?.Score ?? c.Score
            });
            var prompt = $"""
Organize estes cortes de {Profile(options.ContentType)} em séries editoriais para publicação sequencial.
Retorne SOMENTE JSON válido no formato:
[{{"title":"nome concreto da série","summary":"uma frase","clipIds":["id1","id2"],"score":0.0}}]
Regras: uma série precisa ter pelo menos 2 cortes; no máximo 5 por série; não repetir ID dentro da mesma série; agrupe por significado, não apenas palavras iguais; títulos em português do Brasil.
Cortes: {JsonSerializer.Serialize(payload)}
""";
            var generated = Generate<List<EditorialSeries>>(prompt);
            var validIds = clips.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = (generated ?? [])
                .Select(series => new EditorialSeries
                {
                    Id = series.Id,
                    Title = string.IsNullOrWhiteSpace(series.Title) ? "Série" : series.Title.Trim(),
                    Summary = series.Summary?.Trim() ?? "",
                    Score = Clamp(series.Score),
                    ClipIds = series.ClipIds.Where(validIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList()
                })
                .Where(series => series.ClipIds.Count >= 2)
                .Take(8)
                .ToList();
            return result.Count > 0 ? result : fallback;
        }
        catch { return fallback; }
    }

    private static T? Generate<T>(string prompt)
    {
        var payload = JsonSerializer.Serialize(new { model = Model, prompt, stream = false, format = "json" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = Client.PostAsync(Endpoint, content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) return default;
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var outer = JsonDocument.Parse(body);
        if (!outer.RootElement.TryGetProperty("response", out var value)) return default;
        var raw = value.GetString();
        return string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static List<List<TranscriptSegment>> TranscriptChunks(IReadOnlyList<TranscriptSegment> transcript, double duration)
    {
        var result = new List<List<TranscriptSegment>>();
        var end = transcript.Count == 0 ? 0 : transcript.Max(x => x.End);
        for (double start = 0; start < end; start += duration)
        {
            var limit = start + duration;
            var chunk = transcript.Where(x => x.End >= start && x.Start <= limit).ToList();
            if (chunk.Count > 0) result.Add(chunk);
        }
        return result;
    }

    private static string BuildTimedText(IEnumerable<TranscriptSegment> segments, int maxChars)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            var line = $"[{segment.Start:0.00}-{segment.End:0.00}] {segment.Text}\n";
            if (builder.Length + line.Length > maxChars) break;
            builder.Append(line);
        }
        return builder.ToString();
    }

    private static List<EditorialTopic> NormalizeTopics(IEnumerable<EditorialTopic> source, double maxEnd) => source
        .Where(t => !string.IsNullOrWhiteSpace(t.Title) && t.End > t.Start)
        .Select(t => new EditorialTopic
        {
            Id = t.Id,
            Title = t.Title.Trim(),
            Summary = t.Summary?.Trim() ?? "",
            Start = Math.Clamp(t.Start, 0, maxEnd),
            End = Math.Clamp(t.End, 0, maxEnd),
            Confidence = Math.Clamp(t.Confidence, 0, 1),
            Keywords = t.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
        })
        .Where(t => t.End > t.Start)
        .OrderBy(t => t.Start)
        .ToList();

    private static string Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? "" : value.Length <= length ? value : value[..length];
    private static double Clamp(double value) => Math.Round(Math.Clamp(value, 0, 100), 1);
    private static string Profile(string contentType) => contentType switch
    {
        "pregacao" => "pregação cristã e reflexão bíblica",
        "louvor" => "louvor e adoração cristã",
        "podcast" => "podcast e entrevista",
        "aula" => "aula e conteúdo educativo",
        "motivacao" => "motivação e desenvolvimento pessoal",
        "negocios" => "negócios, marketing e vendas",
        "tecnologia" => "tecnologia e programação",
        _ => contentType
    };
}

internal sealed class HeuristicEditorialAiProvider : IEditorialAiProvider
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "para","como","isso","essa","esse","aqui","agora","entao","porque","tambem","muito","gente","sobre","quando","voce","mais","uma","que","com","por","dos","das","não","nao","sim"
    };

    public string Name => "heuristic-local";
    public bool IsAvailable() => true;

    public EditorialIntelligenceResult Analyze(IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options)
    {
        if (transcript.Count == 0) return new EditorialIntelligenceResult { Provider = Name };
        var duration = transcript.Max(x => x.End);
        var target = duration <= 20 * 60 ? 4 * 60 : 7 * 60;
        var topics = new List<EditorialTopic>();
        for (double start = 0; start < duration; start += target)
        {
            var end = Math.Min(duration, start + target);
            var text = string.Join(' ', transcript.Where(s => s.End >= start && s.Start <= end).Select(s => s.Text));
            if (text.Length < 40) continue;
            var keywords = Keywords(text, 5);
            topics.Add(new EditorialTopic
            {
                Title = keywords.Count > 0 ? TitleCase(string.Join(" · ", keywords.Take(3))) : $"Tema {topics.Count + 1}",
                Summary = Summarize(text),
                Start = start,
                End = end,
                Confidence = .55,
                Keywords = keywords
            });
        }
        var globalText = string.Join(' ', transcript.Select(s => s.Text));
        var globalKeywords = Keywords(globalText, 5);
        return new EditorialIntelligenceResult
        {
            Provider = Name,
            MainTheme = globalKeywords.Count > 0 ? TitleCase(string.Join(" · ", globalKeywords.Take(3))) : options.ContentType,
            Summary = Summarize(globalText),
            Topics = topics
        };
    }

    public List<SemanticClipEvaluation> Evaluate(IReadOnlyList<ClipCandidate> clips, ProjectOptions options, IReadOnlyList<EditorialTopic> topics) => clips.Select(clip =>
    {
        var topic = topics
            .Where(t => t.End >= clip.Start && t.Start <= clip.End)
            .OrderByDescending(t => Overlap(t.Start, t.End, clip.Start, clip.End))
            .FirstOrDefault();
        var lower = clip.Transcript.ToLowerInvariant();
        var sentences = clip.Transcript.Count(c => c is '.' or '?' or '!');
        var emotional = Count(lower, "deus", "jesus", "amor", "dor", "cura", "perdão", "perdao", "fé", "fe", "verdade", "propósito", "proposito", "esperança", "esperanca");
        var direct = Count(lower, " você ", " voce ", " seu ", " sua ", " nunca ", " precisa ", " verdade ", " problema ");
        var opening = lower[..Math.Min(lower.Length, 100)];
        var contextPenalty = Count(opening, "como eu disse", "continuando", "isso aqui", "esse ponto", "voltando");
        var standalone = Math.Clamp(55 + sentences * 4 - contextPenalty * 20, 0, 100);
        var share = Math.Clamp(45 + direct * 8 + emotional * 4 + (clip.Score - 50) * .35, 0, 100);
        var emotionalValue = Math.Clamp(35 + emotional * 9, 0, 100);
        var score = Math.Clamp(clip.Score * .55 + standalone * .18 + share * .17 + emotionalValue * .10, 0, 100);
        return new SemanticClipEvaluation
        {
            ClipId = clip.Id,
            Score = Math.Round(score, 1),
            Topic = topic?.Title ?? EditorialDiversityService.Topic(clip.Transcript),
            Shareability = Math.Round(share, 1),
            EmotionalValue = Math.Round(emotionalValue, 1),
            StandaloneClarity = Math.Round(standalone, 1),
            Reason = standalone >= 75 && share >= 70
                ? "ideia independente com bom potencial de compartilhamento"
                : standalone < 50
                    ? "depende mais do contexto anterior"
                    : "conteúdo coerente e aproveitável como corte"
        };
    }).ToList();

    public List<EditorialSeries> Cluster(IReadOnlyList<ClipCandidate> clips, IReadOnlyList<SemanticClipEvaluation> evaluations, ProjectOptions options)
    {
        var byTopic = evaluations
            .Where(e => !string.IsNullOrWhiteSpace(e.Topic))
            .GroupBy(e => e.Topic, StringComparer.OrdinalIgnoreCase);
        var result = new List<EditorialSeries>();
        foreach (var group in byTopic)
        {
            var ids = group.OrderByDescending(e => e.Score).Select(e => e.ClipId).Distinct().Take(5).ToList();
            if (ids.Count < 2) continue;
            result.Add(new EditorialSeries
            {
                Title = group.Key,
                Summary = $"Sequência de cortes sobre {group.Key.ToLowerInvariant()}.",
                ClipIds = ids,
                Score = Math.Round(group.Average(e => e.Score), 1)
            });
        }
        if (result.Count > 0) return result.OrderByDescending(x => x.Score).Take(8).ToList();

        var remaining = clips.OrderByDescending(c => c.Score).ToList();
        while (remaining.Count >= 2)
        {
            var seed = remaining[0];
            var group = remaining.Where(c => EditorialDiversityService.Similarity(seed.Transcript, c.Transcript) >= .12).Take(5).ToList();
            if (group.Count < 2)
            {
                remaining.RemoveAt(0);
                continue;
            }
            result.Add(new EditorialSeries
            {
                Title = EditorialDiversityService.Topic(seed.Transcript),
                Summary = "Cortes semanticamente relacionados.",
                ClipIds = group.Select(c => c.Id).ToList(),
                Score = Math.Round(group.Average(c => c.Score), 1)
            });
            foreach (var item in group) remaining.Remove(item);
        }
        return result.Take(8).ToList();
    }

    private static List<string> Keywords(string text, int count) => Tokens(text)
        .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ThenByDescending(g => g.Key.Length)
        .Select(g => g.Key)
        .Take(count)
        .ToList();

    private static IEnumerable<string> Tokens(string text) => text
        .ToLowerInvariant()
        .Split([' ', ',', '.', '?', '!', ':', ';', '—', '-', '\n', '\r', '(', ')', '"'], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => x.Length > 4 && !StopWords.Contains(x));

    private static string Summarize(string text)
    {
        var clean = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 220 ? clean : clean[..217] + "...";
    }

    private static string TitleCase(string value) => System.Globalization.CultureInfo
        .GetCultureInfo("pt-BR")
        .TextInfo
        .ToTitleCase(value.ToLowerInvariant());

    private static int Count(string value, params string[] needles) => needles.Count(value.Contains);
    private static double Overlap(double aStart, double aEnd, double bStart, double bEnd) => Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));
}
