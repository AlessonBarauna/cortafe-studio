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
        clip.FramingTrack = root.TryGetProperty("track", out var track)
            ? track.EnumerateArray().Select(point => new FramingKeyframe
            {
                Time = point.GetProperty("time").GetDouble(),
                X = point.GetProperty("x").GetDouble()
            }).ToList()
            : [];
        clip.CropFocus = clip.CropX < .38 ? "left" : clip.CropX > .62 ? "right" : "center";
        var detected = root.GetProperty("detected").GetBoolean();
        var coverage = Read(root, "coverage"); var stability = Read(root, "stability"); var prominence = Read(root, "prominence");
        var sceneChanges = root.TryGetProperty("sceneChanges", out var scenes) ? scenes.GetInt32() : 0;
        var duration = Math.Max(1, clip.End - clip.Start); var sceneDensity = sceneChanges / duration * 60;
        var visualScore = Math.Round(Math.Clamp(coverage * 45 + stability * 20 + Math.Min(1, prominence / .08) * 20 + Math.Min(1, sceneDensity / 8) * 15, 0, 100), 1);
        clip.VisualDirection = new VisualDirectionAnalysis
        {
            Analyzed = true, SubjectDetected = detected, SubjectCoverage = Math.Round(coverage, 3), FramingStability = Math.Round(stability, 3),
            SubjectProminence = Math.Round(prominence, 4), SceneChanges = sceneChanges, SceneDensity = Math.Round(sceneDensity, 2), Score = visualScore,
            Recommendation = detected ? coverage >= .65 ? "Acompanhar o rosto principal" : "Alternar rosto e enquadramento seguro" : "Enquadramento central por segurança"
        };
        clip.TransitionStyle = sceneDensity >= 7 ? "dynamic" : sceneDensity >= 3 ? "editorial" : "smooth";
        var bonus = detected ? Math.Clamp((visualScore - 50) * .12, -3, 6) : -2;
        clip.Score = Math.Round(Math.Clamp(clip.Score + bonus, 0, 99), 1);
        clip.Reasons = clip.Reasons.Append(detected ? $"pessoa em foco em {coverage:P0} do take" : "enquadramento central por segurança").Append(sceneChanges > 0 ? $"{sceneChanges} mudança(s) de cena detectada(s)" : "take visualmente contínuo").Distinct().Take(8).ToList();
        await store.SaveAsync(project); return clip;
    }

    private static double Read(JsonElement root, string property) => root.TryGetProperty(property, out var value) ? value.GetDouble() : 0;
}
