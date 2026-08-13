using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialLearningService
{
    private readonly string _file;
    private readonly List<EditorialFeedback> _feedback = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> StopWords = ["para", "com", "que", "uma", "por", "dos", "das", "isso", "você", "mais", "como", "não", "sim", "ele", "ela", "seu", "sua"];

    public EditorialLearningService(IWebHostEnvironment environment)
    {
        _file = Path.Combine(environment.ContentRootPath, "storage", "editorial-feedback.json");
        try { if (File.Exists(_file)) _feedback = JsonSerializer.Deserialize<List<EditorialFeedback>>(File.ReadAllText(_file), JsonOptions) ?? []; } catch { }
    }

    public async Task RecordAsync(VideoProject project, ClipCandidate clip, string decision)
    {
        _feedback.RemoveAll(item => item.ProjectId == project.Id && item.ClipId == clip.Id);
        _feedback.Add(new EditorialFeedback { ProjectId = project.Id, ClipId = clip.Id, Profile = project.Options.ContentType, Decision = decision, Duration = clip.End - clip.Start, Terms = Terms(clip.Transcript).Take(12).ToList(), CreatedAt = DateTime.UtcNow });
        await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(_feedback, JsonOptions));
    }

    public double Adjustment(string profile, string transcript, double duration, out List<string> reasons)
    {
        reasons = []; var relevant = _feedback.Where(item => item.Profile == profile).ToList(); if (relevant.Count < 3) return 0;
        var approved = relevant.Where(item => item.Decision == "approved").ToList(); var rejected = relevant.Where(item => item.Decision == "rejected").ToList(); double score = 0;
        if (approved.Count > 0)
        {
            var preferred = approved.Average(item => item.Duration); var distance = Math.Abs(duration - preferred);
            if (distance <= 12) { score += 7; reasons.Add("duração alinhada às suas aprovações"); }
            var approvedTerms = approved.SelectMany(item => item.Terms).GroupBy(term => term).Where(group => group.Count() >= 2).Select(group => group.Key).ToHashSet();
            var hits = Terms(transcript).Count(approvedTerms.Contains); if (hits > 0) { score += Math.Min(12, hits * 3); reasons.Add("tema semelhante aos cortes aprovados"); }
        }
        var rejectedTerms = rejected.SelectMany(item => item.Terms).GroupBy(term => term).Where(group => group.Count() >= 2).Select(group => group.Key).ToHashSet();
        var rejectedHits = Terms(transcript).Count(rejectedTerms.Contains); if (rejectedHits > 1) { score -= Math.Min(12, rejectedHits * 2); reasons.Add("penalizado pelo histórico de rejeições"); }
        return score;
    }

    public object Profile() => EnumProfiles().Select(profile => { var items = _feedback.Where(item => item.Profile == profile).ToList(); return new { profile, total = items.Count, approved = items.Count(item => item.Decision == "approved"), rejected = items.Count(item => item.Decision == "rejected"), preferredDuration = items.Where(item => item.Decision == "approved").Select(item => (double?)item.Duration).Average() }; });
    public async Task ResetAsync() { _feedback.Clear(); if (File.Exists(_file)) File.Delete(_file); await Task.CompletedTask; }
    private static IEnumerable<string> Terms(string value) => value.ToLowerInvariant().Split([' ', ',', '.', '?', '!', ':', ';', '-', '—'], StringSplitOptions.RemoveEmptyEntries).Select(term => term.Trim()).Where(term => term.Length > 3 && !StopWords.Contains(term)).Distinct();
    private static string[] EnumProfiles() => ["pregacao", "louvor", "podcast", "aula"];
    private sealed class EditorialFeedback { public string ProjectId { get; set; } = ""; public string ClipId { get; set; } = ""; public string Profile { get; set; } = ""; public string Decision { get; set; } = ""; public double Duration { get; set; } public List<string> Terms { get; set; } = []; public DateTime CreatedAt { get; set; } }
}
