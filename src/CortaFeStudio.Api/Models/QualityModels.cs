namespace CortaFeStudio.Api.Models;

public enum QualityStatus { Pass, Warning, Blocked }

public sealed class QualityCheck
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public QualityStatus Status { get; set; }
    public string Detail { get; set; } = "";
    public bool AutoRepairable { get; set; }
}

public sealed class QualityReport
{
    public QualityStatus Status { get; set; }
    public int Score { get; set; }
    public List<QualityCheck> Checks { get; set; } = [];
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public bool CanAutoRepair => Checks.Any(check => check.Status == QualityStatus.Blocked && check.AutoRepairable);
}

public sealed class QualityMediaFacts
{
    public bool FileExists { get; set; }
    public bool Opens { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public double Fps { get; set; }
    public double? LoudnessLufs { get; set; }
    public double? TruePeakDb { get; set; }
    public double LongestSilence { get; set; }
    public double LongestBlackFrame { get; set; }
}
