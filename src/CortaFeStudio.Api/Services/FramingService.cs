using System.Globalization;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class FramingService(ProjectStore store, ToolService tools)
{
    public async Task<ClipCandidate> AnalyzeAsync(VideoProject project, ClipCandidate clip, CancellationToken ct = default)
    {
        var directory = store.ProjectDirectory(project.Id); var media = Path.Combine(directory, project.LocalMedia ?? throw new InvalidOperationException("Mídia original indisponível."));
        var output = Path.Combine(directory, $"faces-{clip.Id}.json");
        await tools.RunAsync(tools.Find("python"), [Path.Combine(tools.Root, "scripts", "detect_faces.py"), media, clip.Start.ToString(CultureInfo.InvariantCulture), clip.End.ToString(CultureInfo.InvariantCulture), output], directory, ct);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, ct)); var root = document.RootElement;
        clip.FaceTrackingAnalyzed = true; clip.CropX = root.GetProperty("cropX").GetDouble();
        clip.CropFocus = clip.CropX < .38 ? "left" : clip.CropX > .62 ? "right" : "center";
        clip.Reasons = clip.Reasons.Append(root.GetProperty("detected").GetBoolean() ? "enquadramento ajustado ao rosto" : "enquadramento central por segurança").Distinct().Take(5).ToList();
        await store.SaveAsync(project); return clip;
    }
}
