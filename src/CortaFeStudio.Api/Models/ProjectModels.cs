using Microsoft.AspNetCore.Http;

namespace CortaFeStudio.Api.Models;

public enum SourceKind { Upload, YouTube }
public enum ProjectStatus { Queued, Acquiring, Transcribing, Analyzing, Ready, Failed, Cancelled }

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
    public string? FailureCode { get; set; }
    public string? YouTubeCookieBrowser { get; set; }
    public double Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Attempt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public List<ProjectFailureAttempt> FailureHistory { get; set; } = [];
    public List<string> CompletedStages { get; set; } = [];
    public string? LastCheckpoint { get; set; }
    public ProjectOptions Options { get; set; } = new();
    public List<TranscriptSegment> Transcript { get; set; } = [];
    public string? TranscriptSource { get; set; }
    public List<ClipCandidate> Clips { get; set; } = [];
    public CandidateAnalysisReport? CandidateAnalysis { get; set; }
    public bool IsRendering { get; set; }
    public int RenderCompleted { get; set; }
    public int RenderTotal { get; set; }
    public bool Archived { get; set; }
    public bool Favorite { get; set; }
    public bool Pinned { get; set; }
    public DateTime? DataPurgedAt { get; set; }
}

public sealed class ProjectFailureAttempt
{
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int Attempt { get; set; }
    public string Stage { get; set; } = "inicial";
    public string Code { get; set; } = "processing-error";
    public string Message { get; set; } = "";
    public bool AutomaticRetry { get; set; }
    public DateTime? RetryAt { get; set; }
}

public sealed class ProjectOptions
{
    public string ContentType { get; set; } = "pregacao";
    public int ClipCount { get; set; } = 20;
    public const int AutomaticMinDuration = 60;
    public const int AutomaticMaxDuration = 75;
    public int MinDuration { get; set; } = AutomaticMinDuration;
    public int MaxDuration { get; set; } = AutomaticMaxDuration;
    public string WhisperModel { get; set; } = "base";
    public string? Topic { get; set; }
    public bool DeleteSourceAfterProcessing { get; set; }
    public static ProjectOptions FromForm(IFormCollection f) => new()
    {
        ContentType = f["contentType"].FirstOrDefault() ?? "pregacao",
        ClipCount = int.TryParse(f["clipCount"], out var count) ? Math.Clamp(count, 1, 20) : 20,
        MinDuration = AutomaticMinDuration,
        MaxDuration = AutomaticMaxDuration,
        WhisperModel = f["whisperModel"].FirstOrDefault() ?? "base",
        Topic = f["topic"].FirstOrDefault()
    };
    public void ApplyAutomaticDuration() { MinDuration = AutomaticMinDuration; MaxDuration = AutomaticMaxDuration; }
}

public sealed class TranscriptSegment { public double Start { get; set; } public double End { get; set; } public string Text { get; set; } = ""; public List<TranscriptWord> Words { get; set; } = []; }
public sealed class TranscriptWord { public double Start { get; set; } public double End { get; set; } public string Word { get; set; } = ""; }
public sealed class ClipCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public double Start { get; set; }
    public double End { get; set; }
    public string Source { get; set; } = "automatic";
    public double Score { get; set; }
    public string HookSentence { get; set; } = "";
    public EditorialScoreBreakdown ScoreBreakdown { get; set; } = new();
    public SocialScoreBreakdown SocialScore { get; set; } = new();
    public string Transcript { get; set; } = "";
    public string Title { get; set; } = "Momento que merece ser ouvido";
    public bool TitleEditedByUser { get; set; }
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
    public double CropX { get; set; } = .5;
    public List<FramingKeyframe> FramingTrack { get; set; } = [];
    public string LayoutMode { get; set; } = "fill";
    public string OutputPreset { get; set; } = "vertical";
    public bool FaceTrackingAnalyzed { get; set; }
    public VisualDirectionAnalysis VisualDirection { get; set; } = new();
    public string TransitionStyle { get; set; } = "smooth";
    public string SubtitleStyle { get; set; } = "impact";
    public SubtitleTrack? SubtitleTrack { get; set; }
    public bool BrandFrameEnabled { get; set; } = true;
    public string BrandTheme { get; set; } = "amado-jesus";
    public bool WatermarkEnabled { get; set; } = true;
    public string WatermarkText { get; set; } = "AJ  |  AMADO JESUS";
    public double WatermarkOpacity { get; set; } = .82;
    public string CoverAccent { get; set; } = "#C7A35A";
    public string CoverPosition { get; set; } = "bottom";
    public double? CoverTimestamp { get; set; }
    public string? EditedTranscript { get; set; }
    public List<ClipVariant> Variants { get; set; } = [];
    public string? WinningVariantId { get; set; }
    public QualityReport? QualityReport { get; set; }
    public PlatformMetadata PlatformMetadata { get; set; } = new();
    public bool SilenceTrimmingEnabled { get; set; } = true;
    public SilenceTrimPlan? SilenceTrimPlan { get; set; }
    public string? LastRenderFingerprint { get; set; }
    public bool RenderOutdated { get; set; }
    public double PlaybackSpeed { get; set; } = 1;
    public string? PreviewPath { get; set; }
    public string? LastPreviewFingerprint { get; set; }
    public string DiversityTopic { get; set; } = "";
    public double DiversityScore { get; set; }
    public string TikTokWorkflowStatus { get; set; } = "draft";
    public DateTimeOffset? TikTokScheduledAt { get; set; }
    public DateTimeOffset? TikTokPublishedAt { get; set; }
}
public sealed class CandidateAnalysisReport
{
    public int RequestedClips { get; set; }
    public int TranscriptSegments { get; set; }
    public int RawCandidates { get; set; }
    public int RejectedByDuration { get; set; }
    public int RejectedByOverlap { get; set; }
    public int RejectedByScore { get; set; }
    public int RejectedByContext { get; set; }
    public int RejectedByIncompleteEnding { get; set; }
    public int FinalCandidates { get; set; }
    public List<string> Warnings { get; set; } = [];
}
public sealed class SubtitleTrack
{
    public bool Enabled { get; set; } = true;
    public string Style { get; set; } = "impact";
    public double OffsetSeconds { get; set; }
    public bool AutoGenerated { get; set; } = true;
    public bool EditedByUser { get; set; }
    public double Confidence { get; set; }
    public string ConfidenceLabel { get; set; } = "não avaliada";
    public string? QualityWarning { get; set; }
    public List<SubtitleBlock> Blocks { get; set; } = [];
}
public sealed class SubtitleBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<TranscriptWord> Words { get; set; } = [];
}
public sealed class ClipVariant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Label { get; set; } = "A";
    public double Start { get; set; }
    public double End { get; set; }
    public string HookSentence { get; set; } = "";
    public double PunchInIntensity { get; set; } = 1;
    public string SubtitleDensity { get; set; } = "balanced";
    public double SocialScore { get; set; }
    public double VariantScore { get; set; }
    public bool Winner { get; set; }
}
public sealed class FramingKeyframe
{
    public double Time { get; set; }
    public double X { get; set; } = .5;
}
public sealed class VisualDirectionAnalysis
{
    public bool Analyzed { get; set; }
    public bool SubjectDetected { get; set; }
    public double SubjectCoverage { get; set; }
    public double FramingStability { get; set; }
    public double SubjectProminence { get; set; }
    public int SceneChanges { get; set; }
    public List<double> SceneTransitionPoints { get; set; } = [];
    public double SceneDensity { get; set; }
    public double Score { get; set; }
    public string Recommendation { get; set; } = "Enquadramento central";
}
public sealed class SocialScoreBreakdown
{
    public double Hook { get; set; }
    public double Retention { get; set; }
    public double Conclusion { get; set; }
    public double Potential { get; set; }
}
public sealed class EditorialScoreBreakdown
{
    public double Base { get; set; } = 38;
    public double Hook { get; set; }
    public double OpeningAdjustment { get; set; }
    public double Completion { get; set; }
    public double Contrast { get; set; }
    public double Conclusion { get; set; }
    public double Structure { get; set; }
    public double ContextPenalty { get; set; }
    public double ProfileRelevance { get; set; }
    public double TopicRelevance { get; set; }
    public double LengthAdjustment { get; set; }
    public double Impact { get; set; }
    public double Clarity { get; set; }
    public double Learning { get; set; }

    public double Total => Math.Round(
        Math.Clamp(
            Base +
            Hook +
            OpeningAdjustment +
            Completion +
            Contrast +
            Conclusion +
            Structure +
            ContextPenalty +
            ProfileRelevance +
            TopicRelevance +
            LengthAdjustment +
            Impact +
            Clarity +
            Learning,
            0,
            99),
        1);
}

public sealed class UrlProjectRequest
{
    public string Url { get; set; } = "";
    public string? Name { get; set; }
    public ProjectOptions? Options { get; set; }
}
public sealed class UrlBatchProjectRequest
{
    public List<string> Urls { get; set; } = [];
    public string? Name { get; set; }
    public ProjectOptions? Options { get; set; }
}
public record ClipUpdate(double? Start, double? End, string? Title, string? CoverText, string? Caption, bool? Approved, string? CropFocus, string? SubtitleStyle, string? CoverAccent, string? CoverPosition, double? CoverTimestamp, string? EditedTranscript, double? CropX, string? LayoutMode, string? OutputPreset, bool? BrandFrameEnabled = null, string? BrandTheme = null, bool? WatermarkEnabled = null, string? WatermarkText = null, double? WatermarkOpacity = null, double? PlaybackSpeed = null, bool? SilenceTrimmingEnabled = null, string? TransitionStyle = null);
public record ManualClipRequest(double Start, double End);
public record SplitClipRequest(double At);
public record RestartFromRequest(string Stage);
public record RetryProjectRequest(string? Browser = null);
public record CleanupProjectRequest(bool DeleteSource = false);
public record ReanalyzeRequest(string? Topic, int? ClipCount, bool Render = false);
public record ClipFeedbackRequest(string Feedback);
public record BatchFeedbackRequest(List<string> ClipIds, string Feedback);
public record TikTokWorkflowUpdate(string Status, DateTimeOffset? ScheduledAt = null);
public record LibraryProjectUpdate(bool? Favorite = null, bool? Pinned = null);
public record BatchProjectDataRequest(List<string> ProjectIds);

public enum RetentionCleanupMode { ProjectData, FullProject }
public sealed class RetentionPolicy
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 7;
    public RetentionCleanupMode Mode { get; set; } = RetentionCleanupMode.ProjectData;
    public bool ProtectFavorites { get; set; } = true;
    public bool ProtectPinned { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}
public sealed record RetentionPolicyUpdate(bool Enabled, int RetentionDays, RetentionCleanupMode Mode, bool ProtectFavorites = true, bool ProtectPinned = true);
public sealed record RetentionCandidate(string ProjectId, string Name, DateTime ReferenceDate, long EstimatedBytes, bool WillDeleteProject);
public sealed record RetentionPreview(RetentionPolicy Policy, DateTime Cutoff, IReadOnlyList<RetentionCandidate> Candidates, long EstimatedBytes);
public sealed record RetentionExecution(int Processed, long FreedBytes, IReadOnlyList<string> ProjectIds, DateTime CompletedAt);
