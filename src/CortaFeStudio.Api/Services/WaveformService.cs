using System.Text.Json;

namespace CortaFeStudio.Api.Services;

public sealed class WaveformService(ToolService tools, ProjectStore projects)
{
    private const int SampleRate = 100;
    private const int TargetPoints = 1200;

    public async Task<IReadOnlyList<double>> GetAsync(string projectId, CancellationToken ct = default)
    {
        var project = projects.Get(projectId) ?? throw new KeyNotFoundException("Projeto não encontrado.");
        if (string.IsNullOrWhiteSpace(project.LocalMedia)) throw new InvalidOperationException("A mídia original não está disponível.");
        var source = projects.ResolveAsset(projectId, project.LocalMedia) ?? throw new FileNotFoundException("A mídia original foi removida.");
        var directory = projects.ProjectDirectory(projectId);
        var cache = Path.Combine(directory, $"waveform-{projectId}.json");
        if (File.Exists(cache)) return JsonSerializer.Deserialize<List<double>>(await File.ReadAllTextAsync(cache, ct)) ?? [];

        var raw = Path.Combine(directory, $"waveform-{projectId}.f32");
        try
        {
            await tools.RunAsync(tools.Find("ffmpeg"), ["-y", "-i", source, "-vn", "-ac", "1", "-ar", SampleRate.ToString(), "-f", "f32le", raw], directory, ct);
            var bytes = await File.ReadAllBytesAsync(raw, ct);
            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
            var points = Downsample(samples, TargetPoints);
            await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(points), ct);
            return points;
        }
        finally { if (File.Exists(raw)) File.Delete(raw); }
    }

    public static IReadOnlyList<double> Downsample(IReadOnlyList<float> samples, int targetPoints)
    {
        if (samples.Count == 0 || targetPoints <= 0) return [];
        var count = Math.Min(samples.Count, targetPoints);
        var result = new double[count];
        for (var point = 0; point < count; point++)
        {
            var start = point * samples.Count / count;
            var end = Math.Max(start + 1, (point + 1) * samples.Count / count);
            var peak = 0d;
            for (var index = start; index < end; index++) peak = Math.Max(peak, Math.Abs(samples[index]));
            result[point] = peak;
        }
        var maximum = result.Max();
        if (maximum > 0) for (var index = 0; index < result.Length; index++) result[index] = Math.Round(result[index] / maximum, 4);
        return result;
    }
}
