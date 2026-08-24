using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class PerformanceLearningService
{
    private readonly string _file;
    private readonly ILogger<PerformanceLearningService> _logger;
    private readonly List<ContentPerformance> _items = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PerformanceLearningService(IWebHostEnvironment environment, ILogger<PerformanceLearningService> logger)
    {
        _logger = logger;
        _file = Path.Combine(environment.ContentRootPath, "storage", "content-performance.json");
        try { if (File.Exists(_file)) _items = JsonSerializer.Deserialize<List<ContentPerformance>>(File.ReadAllText(_file), JsonOptions) ?? []; }
        catch (Exception ex)
        {
            _items = [];
            _logger.LogWarning(ex, "Nao foi possivel carregar o historico de desempenho em {File}", _file);
        }
    }

    public IReadOnlyList<ContentPerformance> List() => _items.OrderByDescending(item => item.PublishedAt).ToList();

    public async Task<ContentPerformance> RecordAsync(VideoProject project, ClipCandidate clip, RecordPerformanceRequest request)
    {
        Validate(request.Snapshot);
        await _lock.WaitAsync();
        try
        {
            var item = _items.FirstOrDefault(value => value.ProjectId == project.Id && value.ClipId == clip.Id && value.VariantId == request.VariantId && value.Platform == request.Platform);
            if (item is null)
            {
                item = new ContentPerformance { ProjectId = project.Id, ClipId = clip.Id, VariantId = request.VariantId, Platform = request.Platform, PublishedAt = request.PublishedAt ?? DateTimeOffset.UtcNow, Duration = clip.End - clip.Start, EditorialProfile = clip.EditorialProfile, SubtitleStyle = clip.SubtitleStyle, Topic = Topic(clip), HookScore = clip.SocialScore.Hook };
                _items.Add(item);
            }
            item.Snapshots.RemoveAll(snapshot => snapshot.CapturedAt == request.Snapshot.CapturedAt); item.Snapshots.Add(request.Snapshot); item.Snapshots = item.Snapshots.OrderBy(snapshot => snapshot.CapturedAt).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!); await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(_items, JsonOptions)); return item;
        }
        finally { _lock.Release(); }
    }

    public PerformanceInsights Insights(string? profile = null)
    {
        var samples = _items.Where(item => item.Snapshots.Count > 0 && (string.IsNullOrWhiteSpace(profile) || item.EditorialProfile == profile)).Select(item => new { Item = item, Snapshot = item.Snapshots.MaxBy(snapshot => snapshot.CapturedAt)!, Score = PerformanceScore(item.Snapshots.MaxBy(snapshot => snapshot.CapturedAt)!) }).ToList();
        if (samples.Count == 0) return new PerformanceInsights { Recommendations = ["Registre as primeiras metricas para iniciar o aprendizado local."] };
        var threshold = samples.OrderByDescending(sample => sample.Score).ElementAt(Math.Max(0, (int)Math.Ceiling(samples.Count * .35) - 1)).Score;
        var winners = samples.Where(sample => sample.Score >= threshold).ToList();
        var insights = new PerformanceInsights { Samples = samples.Count, PreferredDuration = Math.Round(winners.Average(sample => sample.Item.Duration), 1), BestTopic = Best(winners, sample => sample.Item.Topic), BestSubtitleStyle = Best(winners, sample => sample.Item.SubtitleStyle), BestEditorialProfile = Best(winners, sample => sample.Item.EditorialProfile), BestPlatform = winners.GroupBy(sample => sample.Item.Platform).OrderByDescending(group => group.Average(item => item.Score)).First().Key, BestPublishingHour = winners.GroupBy(sample => sample.Item.PublishedAt.ToLocalTime().Hour).OrderByDescending(group => group.Average(item => item.Score)).First().Key, RecommendedHookScore = Math.Round(winners.Average(sample => sample.Item.HookScore), 1) };
        insights.Recommendations = BuildRecommendations(insights); return insights;
    }

    public static double PerformanceScore(PerformanceSnapshot snapshot)
    {
        if (snapshot.Views <= 0) return 0;
        var engagement = (snapshot.Likes + snapshot.Comments * 2d + snapshot.Shares * 3d) / snapshot.Views * 100;
        var retention = Math.Clamp(snapshot.RetentionPercent ?? 0, 0, 100);
        var reach = Math.Min(100, Math.Log10(snapshot.Views + 1) * 20);
        return Math.Round(Math.Clamp(retention * .5 + Math.Min(100, engagement * 5) * .3 + reach * .2, 0, 100), 1);
    }

    private static string? Best<T>(IEnumerable<T> values, Func<T, string> selector) => values.Where(value => !string.IsNullOrWhiteSpace(selector(value))).GroupBy(selector, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
    private static List<string> BuildRecommendations(PerformanceInsights value) => [$"Priorize cortes proximos de {value.PreferredDuration:0}s.", $"O estilo de legenda com melhor sinal e {value.BestSubtitleStyle}.", $"O horario local mais promissor e {value.BestPublishingHour:00}:00.", $"Mantenha Hook Score proximo ou acima de {value.RecommendedHookScore:0}."];
    private static string Topic(ClipCandidate clip) => clip.Hashtags.FirstOrDefault()?.TrimStart('#') ?? clip.EditorialProfile;
    private static void Validate(PerformanceSnapshot snapshot)
    {
        if (snapshot.Views < 0 || snapshot.Likes < 0 || snapshot.Comments < 0 || snapshot.Shares < 0) throw new ArgumentOutOfRangeException(nameof(snapshot), "Metricas nao podem ser negativas.");
        if (snapshot.RetentionPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(snapshot), "Retencao deve estar entre 0 e 100.");
    }
}
