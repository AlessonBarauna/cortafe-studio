using Microsoft.AspNetCore.Http;

namespace CortaFeStudio.Api.Models;

public enum SourceKind { Upload, YouTube }
public enum ProjectStatus { Queued, Acquiring, Transcribing, Analyzing, Ready, Failed }

public sealed class VideoProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Novo projeto";
    public SourceKind SourceKind { get; set; }
    public string Source { get; set; } = "";
    public string? LocalMedia { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Queued;
    public int Progress { get; set; }
    public string Stage { get; set; } = "Na fila";
    public string? Error { get; set; }
    public double Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Attempt { get; set; }
    public List<string> CompletedStages { get; set; } = [];
    public string? LastCheckpoint { get; set; }
    public ProjectOptions Options { get; set; } = new();
    public List<TranscriptSegment> Transcript { get; set; } = [];
    public string? TranscriptSource { get; set; }
    public List<ClipCandidate> Clips { get; set; } = [];
    public bool Archived { get; set; }
}

public sealed class ProjectOptions
{
    public string ContentType { get; set; } = "pregacao";
    public int ClipCount { get; set; } = 5;
    public int MinDuration { get; set; } = 30;
    public int MaxDuration { get; set; } = 75;
    public string WhisperModel { get; set; } = "base";
    public string? Topic { get; set; }
    public bool DeleteSourceAfterProcessing { get; set; }
    public static ProjectOptions FromForm(IFormCollection f) => new()
    {
        ContentType = f["contentType"].FirstOrDefault() ?? "pregacao",
        ClipCount = int.TryParse(f["clipCount"], out var count) ? Math.Clamp(count, 1, 20) : 5,
        MinDuration = int.TryParse(f["minDuration"], out var min) ? Math.Clamp(min, 10, 300) : 30,
        MaxDuration = int.TryParse(f["maxDuration"], out var max) ? Math.Clamp(max, 15, 600) : 75,
        WhisperModel = f["whisperModel"].FirstOrDefault() ?? "base",
        Topic = f["topic"].FirstOrDefault()
    };
}

public sealed class TranscriptSegment { public double Start { get; set; } public double End { get; set; } public string Text { get; set; } = ""; public List<TranscriptWord> Words { get; set; } = []; }
public sealed class TranscriptWord { public double Start { get; set; } public double End { get; set; } public string Word { get; set; } = ""; }
public sealed class ClipCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public double Start { get; set; }
    public double End { get; set; }
    public double Score { get; set; }
    public string Transcript { get; set; } = "";
    public string Title { get; set; } = "Momento que merece ser ouvido";
    public string CoverText { get; set; } = "OUÇA ISSO";
    public string Caption { get; set; } = "Uma mensagem para guardar e compartilhar. ✨";
    public List<string> Hashtags { get; set; } = ["#fé", "#mensagem", "#shorts"];
    public bool Approved { get; set; } = true;
    public string? CoverPath { get; set; }
    public string? VideoPath { get; set; }
    public List<string> Reasons { get; set; } = [];
    public string EditorialProfile { get; set; } = "pregacao";
    public string Feedback { get; set; } = "pending";
    public string CropFocus { get; set; } = "center";
    public string SubtitleStyle { get; set; } = "impact";
    public string CoverAccent { get; set; } = "#F0B44D";
    public string CoverPosition { get; set; } = "bottom";
    public double? CoverTimestamp { get; set; }
    public string? EditedTranscript { get; set; }
}

public sealed class UrlProjectRequest
{
    public string Url { get; set; } = "";
    public string? Name { get; set; }
    public ProjectOptions? Options { get; set; }
}
public record ClipUpdate(double? Start, double? End, string? Title, string? CoverText, string? Caption, bool? Approved, string? CropFocus, string? SubtitleStyle, string? CoverAccent, string? CoverPosition, double? CoverTimestamp, string? EditedTranscript);
public record SplitClipRequest(double At);
public record RestartFromRequest(string Stage);
public record CleanupProjectRequest(bool DeleteSource = false);
public record ReanalyzeRequest(string? Topic, int? ClipCount, bool Render = false);
public record ClipFeedbackRequest(string Feedback);
