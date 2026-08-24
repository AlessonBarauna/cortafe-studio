using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class HardwareEncoderDetector(ToolService tools, ILogger<HardwareEncoderDetector> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RenderEncoderProfile? _cached;
    public static RenderEncoderProfile Cpu => new();

    public async Task<RenderEncoderProfile> DetectAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;
            var encoders = await tools.CaptureDiagnosticAsync(tools.Find("ffmpeg"), ["-hide_banner", "-encoders"], null, ct);
            foreach (var candidate in Profiles.Where(item => encoders.Contains(item.Codec, StringComparison.Ordinal)))
            {
                if (!await TestAsync(candidate, ct)) continue;
                _cached = candidate; logger.LogInformation("[Encoder] {Codec} validado por encode real", candidate.Codec); return candidate;
            }
            _cached = Cpu; logger.LogInformation("[Encoder] fallback={Codec}", _cached.Codec); return _cached;
        }
        finally { _lock.Release(); }
    }

    public void Invalidate() => _cached = null;

    private async Task<bool> TestAsync(RenderEncoderProfile profile, CancellationToken ct)
    {
        try
        {
            var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=128x128:r=10:d=0.4", "-frames:v", "4", "-c:v", profile.Codec };
            arguments.AddRange(profile.Arguments); arguments.AddRange(["-f", "null", "-"]);
            await tools.RunAsync(tools.Find("ffmpeg"), arguments, null, ct); return true;
        }
        catch (Exception ex) { logger.LogDebug("[Encoder] {Codec} indisponivel: {Reason}", profile.Codec, ex.Message); return false; }
    }

    public static IReadOnlyList<RenderEncoderProfile> Profiles { get; } =
    [
        new() { Name = "NVIDIA NVENC", Codec = "h264_nvenc", HardwareAccelerated = true, Arguments = ["-preset", "p5", "-cq", "19", "-b:v", "0"] },
        new() { Name = "Intel Quick Sync", Codec = "h264_qsv", HardwareAccelerated = true, Arguments = ["-preset", "medium", "-global_quality", "19"] },
        new() { Name = "AMD AMF", Codec = "h264_amf", HardwareAccelerated = true, Arguments = ["-quality", "quality", "-rc", "cqp", "-qp_i", "19", "-qp_p", "19"] }
    ];
}
