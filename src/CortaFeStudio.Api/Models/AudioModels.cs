namespace CortaFeStudio.Api.Models;

public enum AudioProfile { VoiceClean, VoiceNoisy, VoiceWithMusic, Podcast, Worship, Music, LowVolume, Clipped }

public sealed class AudioAnalysis
{
    public AudioProfile Profile { get; set; } = AudioProfile.VoiceClean;
    public double? MeanVolumeDb { get; set; }
    public double? PeakVolumeDb { get; set; }
    public double SilenceRatio { get; set; }
    public string Reason { get; set; } = "Fallback seguro para voz";
}

public sealed class AudioProcessingProfile
{
    public AudioProfile Profile { get; init; }
    public string Filter { get; init; } = "";
    public string TargetLoudness { get; init; } = "-16 LUFS";
}
