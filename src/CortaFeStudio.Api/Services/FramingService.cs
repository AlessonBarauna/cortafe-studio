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
        await tools.RunAsync(tools.Find("python"), [Path.Combine(tools.Root, "scripts", "detect_faces.py"), media, clip.Start.ToString(CultureInfo.InvariantCulture), clip.End.ToString(CultureInfo.InvariantCulture), output, project.Options.ContentType], directory, ct);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, ct)); var root = document.RootElement;
        clip.FaceTrackingAnalyzed = true; clip.FramingAnalysisVersion = 2; clip.CropX = root.GetProperty("cropX").GetDouble();
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
        var multiPerson = root.TryGetProperty("multiPerson", out var multi) && multi.GetBoolean();
        var speakerSwitches = root.TryGetProperty("speakerSwitches", out var switches) ? switches.GetInt32() : 0;
        var activeSpeakerConfidence = Read(root, "activeSpeakerConfidence");
        if (root.TryGetProperty("participantCenters", out var participantCenters))
        {
            if (participantCenters.TryGetProperty("left", out var left)) clip.SplitLeftX = left.GetDouble();
            if (participantCenters.TryGetProperty("right", out var right)) clip.SplitRightX = right.GetDouble();
        }
        if (multiPerson && activeSpeakerConfidence < .35 && clip.LayoutMode == "fill") clip.LayoutMode = "split";
        var sceneChanges = root.TryGetProperty("sceneChanges", out var scenes) ? scenes.GetInt32() : 0;
        var sceneTimes = root.TryGetProperty("sceneTimes", out var times)
            ? times.EnumerateArray().Select(value => value.GetDouble()).Where(value => value >= 1 && value <= Math.Max(1, clip.End - clip.Start - 1)).ToList()
            : [];
        var duration = Math.Max(1, clip.End - clip.Start); var sceneDensity = sceneChanges / duration * 60;
        var visualScore = Math.Round(Math.Clamp(coverage * 45 + stability * 20 + Math.Min(1, prominence / .08) * 20 + Math.Min(1, sceneDensity / 8) * 15, 0, 100), 1);
        clip.VisualDirection = new VisualDirectionAnalysis
        {
            Analyzed = true, SubjectDetected = detected, SubjectCoverage = Math.Round(coverage, 3), FramingStability = Math.Round(stability, 3),
            SubjectProminence = Math.Round(prominence, 4), SceneChanges = sceneChanges, SceneTransitionPoints = sceneTimes, SceneDensity = Math.Round(sceneDensity, 2), Score = visualScore,
            Recommendation = multiPerson
                ? activeSpeakerConfidence >= .35 ? "Acompanhar automaticamente quem está falando" : "Manter o participante ativo em foco"
                : detected ? coverage >= .65 ? "Acompanhar o rosto principal" : "Alternar rosto e enquadramento seguro" : "Enquadramento central por segurança"
        };
        clip.TransitionStyle = sceneDensity >= 7 ? "dynamic" : sceneDensity >= 3 ? "editorial" : "smooth";
        var bonus = detected ? Math.Clamp((visualScore - 50) * .12, -3, 6) : -2;
        clip.Score = Math.Round(Math.Clamp(clip.Score + bonus, 0, 99), 1);
        clip.Reasons = clip.Reasons
            .Append(detected ? $"pessoa em foco em {coverage:P0} do take" : "enquadramento central por segurança")
            .Append(multiPerson ? $"locutor ativo acompanhado ({speakerSwitches} troca(s) de participante)" : "um participante principal detectado")
            .Append(sceneChanges > 0 ? $"{sceneChanges} mudança(s) de cena detectada(s)" : "take visualmente contínuo").Distinct().Take(8).ToList();

        // A análise visual é o ponto em que já temos rosto, densidade de cenas e
        // score editorial. O diretor combina tudo e define ritmo, legenda, layout
        // seguro e estilo de transição antes do render.
        new AiEditingDirectorService().Direct(clip, project.Options);

        await store.SaveAsync(project); return clip;
    }

    private static double Read(JsonElement root, string property) => root.TryGetProperty(property, out var value) ? value.GetDouble() : 0;
}
