namespace CortaFeStudio.Api.Models;

public sealed class AutopilotConfiguration
{
    public bool Enabled { get; set; }
    public int PollMinutes { get; set; } = 15;
    public List<AutopilotSource> Sources { get; set; } = [];
    public DateTime? LastCheckAt { get; set; }
    public string? LastMessage { get; set; }
}

public sealed class AutopilotSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "Canal";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string ContentType { get; set; } = "pregacao";
    public string WhisperModel { get; set; } = "base";
    public int ClipCount { get; set; } = 20;
    public string? Topic { get; set; }
    public string? LastSeenMediaId { get; set; }
    public string? LastQueuedMediaId { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class AutopilotConfigurationUpdate
{
    public bool Enabled { get; set; }
    public int PollMinutes { get; set; } = 15;
    public List<AutopilotSource> Sources { get; set; } = [];
}

public sealed class AutopilotCheckResult
{
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public int SourcesChecked { get; set; }
    public int ProjectsQueued { get; set; }
    public List<string> Messages { get; set; } = [];
}
