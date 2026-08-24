namespace CortaFeStudio.Api.Models;

public sealed class SilenceCut
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Duration => Math.Max(0, End - Start);
}

public sealed class SilenceTrimPlan
{
    public bool Applied => Cuts.Count > 0;
    public double OriginalDuration { get; set; }
    public double FinalDuration { get; set; }
    public double RemovedDuration { get; set; }
    public List<SilenceCut> Cuts { get; set; } = [];
    public string Reason { get; set; } = "Nenhuma pausa longa encontrada";
}
