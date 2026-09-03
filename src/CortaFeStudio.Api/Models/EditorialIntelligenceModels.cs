namespace CortaFeStudio.Api.Models;

public sealed class EditorialTopic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "Tema";
    public string Summary { get; set; } = "";
    public double Start { get; set; }
    public double End { get; set; }
    public double Confidence { get; set; }
    public List<string> Keywords { get; set; } = [];
}

public sealed class EditorialSeries
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "Série";
    public string Summary { get; set; } = "";
    public List<string> ClipIds { get; set; } = [];
    public double Score { get; set; }
}

public sealed class SemanticClipEvaluation
{
    public string ClipId { get; set; } = "";
    public double Score { get; set; }
    public string Reason { get; set; } = "";
    public string Topic { get; set; } = "";
    public double Shareability { get; set; }
    public double EmotionalValue { get; set; }
    public double StandaloneClarity { get; set; }
}

public sealed class EditorialIntelligenceResult
{
    public string Provider { get; set; } = "heuristic";
    public string MainTheme { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<EditorialTopic> Topics { get; set; } = [];
    public List<SemanticClipEvaluation> Evaluations { get; set; } = [];
    public List<EditorialSeries> Series { get; set; } = [];
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
