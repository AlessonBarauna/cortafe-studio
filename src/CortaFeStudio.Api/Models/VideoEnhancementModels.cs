namespace CortaFeStudio.Api.Models;

public enum VideoEnhancementKind { Neutral, Dark, LowSaturation, LowContrast, Noisy, WashedOut, Overexposed }

public sealed class VideoAnalysis
{
    public double? LumaAverage { get; set; }
    public double? LumaLow { get; set; }
    public double? LumaHigh { get; set; }
    public double? SaturationAverage { get; set; }
    public VideoEnhancementKind Kind { get; set; }
    public string Reason { get; set; } = "Imagem dentro da faixa segura";
}

public sealed class VideoEnhancementProfile
{
    public VideoEnhancementKind Kind { get; init; }
    public string Filter { get; init; } = "null";
    public bool Applied => Kind != VideoEnhancementKind.Neutral;
}
