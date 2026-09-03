using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class EditorialIntelligencePipeline
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static EditorialIntelligenceResult BuildMap(IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options)
    {
        if (transcript.Count == 0) return new EditorialIntelligenceResult();
        var key = CacheKey(transcript, options);
        var path = CachePath(key, "map");
        var cached = Read<EditorialIntelligenceResult>(path);
        if (cached is { Topics.Count: > 0 }) return cached;

        var provider = EditorialAiProviderFactory.CreateDefault();
        var result = provider.Analyze(transcript, options);
        result.Provider = provider.Name;
        Write(path, result);
        return result;
    }

    public static List<List<TranscriptSegment>> BuildTopicChunks(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<EditorialTopic> topics,
        double fallbackDuration,
        double fallbackOverlap)
    {
        if (topics.Count > 0)
        {
            var semanticChunks = topics
                .Where(topic => topic.End > topic.Start)
                .OrderBy(topic => topic.Start)
                .Select(topic => transcript.Where(segment => segment.End >= Math.Max(0, topic.Start - 5) && segment.Start <= topic.End + 5).ToList())
                .Where(chunk => chunk.Count >= 3)
                .ToList();
            if (semanticChunks.Count > 0) return semanticChunks;
        }

        var result = new List<List<TranscriptSegment>>();
        var duration = transcript.Count == 0 ? 0 : transcript.Max(segment => segment.End);
        for (double start = 0; start < duration; start += fallbackDuration - fallbackOverlap)
        {
            var end = Math.Min(duration, start + fallbackDuration);
            var segments = transcript.Where(segment => segment.End >= start && segment.Start <= end).ToList();
            if (segments.Count > 0) result.Add(segments);
            if (end >= duration) break;
        }
        return result;
    }

    public static EditorialIntelligenceResult EnrichCandidates(
        EditorialIntelligenceResult intelligence,
        IList<ClipCandidate> clips,
        ProjectOptions options)
    {
        if (clips.Count == 0) return intelligence;
        var provider = EditorialAiProviderFactory.CreateDefault();
        var evaluations = provider.Evaluate(clips, options, intelligence.Topics);
        intelligence.Evaluations = evaluations;

        var evaluationsById = evaluations
            .Where(item => !string.IsNullOrWhiteSpace(item.ClipId))
            .GroupBy(item => item.ClipId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Score).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var clip in clips)
        {
            if (!evaluationsById.TryGetValue(clip.Id, out var semantic)) continue;
            var original = clip.Score;
            var semanticDelta = (semantic.Score - 50) * .18;
            clip.Score = Math.Round(Math.Clamp(original + semanticDelta, 0, 99), 1);
            if (!string.IsNullOrWhiteSpace(semantic.Topic)) clip.DiversityTopic = semantic.Topic;
            var explanation = $"IA editorial {semantic.Score:0}/100: {semantic.Reason}";
            clip.Reasons = clip.Reasons
                .Prepend(explanation)
                .Append($"compartilhamento {semantic.Shareability:0}/100 · emoção {semantic.EmotionalValue:0}/100 · clareza isolada {semantic.StandaloneClarity:0}/100")
                .Distinct()
                .Take(8)
                .ToList();
        }

        intelligence.Series = provider.Cluster(clips, evaluations, options);
        MarkSeries(clips, intelligence.Series);
        intelligence.Provider = provider.Name;
        intelligence.GeneratedAt = DateTime.UtcNow;
        Write(CachePath(CacheKeyFromClips(clips, options), "selection"), intelligence);
        return intelligence;
    }

    public static void MarkSeries(IList<ClipCandidate> clips, IReadOnlyList<EditorialSeries> series)
    {
        var byId = clips.ToDictionary(clip => clip.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var group in series)
        {
            foreach (var id in group.ClipIds)
            {
                if (!byId.TryGetValue(id, out var clip)) continue;
                clip.Reasons = clip.Reasons
                    .Append($"série sugerida: {group.Title}")
                    .Distinct()
                    .Take(8)
                    .ToList();
            }
        }
    }

    public static void PersistReadableReport(EditorialIntelligenceResult result)
    {
        try
        {
            var directory = CacheDirectory();
            Directory.CreateDirectory(directory);
            var latest = Path.Combine(directory, "latest-editorial-intelligence.json");
            Write(latest, result);
        }
        catch { }
    }

    private static string CacheKey(IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options)
    {
        var builder = new StringBuilder(options.ContentType).Append('|').Append(options.Topic).Append('|');
        foreach (var segment in transcript)
            builder.Append(segment.Start.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(segment.Text).Append('|');
        return Hash(builder.ToString());
    }

    private static string CacheKeyFromClips(IEnumerable<ClipCandidate> clips, ProjectOptions options)
    {
        var value = options.ContentType + "|" + string.Join('|', clips.OrderBy(c => c.Start).Select(c => $"{c.Start:0.00}:{c.End:0.00}:{c.Transcript}"));
        return Hash(value);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20].ToLowerInvariant();
    private static string CachePath(string key, string suffix) => Path.Combine(CacheDirectory(), $"{key}-{suffix}.json");
    private static string CacheDirectory() => Path.Combine(AppContext.BaseDirectory, "storage", "editorial-cache");

    private static T? Read<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
        }
        catch { }
    }
}
