using System.Globalization;
using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class MediaPipeline(ProjectStore store, ToolService tools, IHttpClientFactory http, LongVideoEditorialAnalyzer editorial, AudioAnalyzer audioAnalyzer, VideoEnhancementService videoEnhancement, HardwareEncoderDetector encoderDetector, QualityGateService qualityGate, ProductionWorkLimiter workLimiter, StorageCapacityService storageCapacity, SilenceTrimmingService silenceTrimming, ILogger<MediaPipeline> logger)
{
    public async Task ProcessAsync(VideoProject p, CancellationToken ct)
    {
        var dir = store.ProjectDirectory(p.Id);
        await Stage(p, ProjectStatus.Acquiring, 5, "Preparando a mídia");
        var transcriptFile = Path.Combine(dir, "transcript.json");
        if (p.Transcript.Count == 0 && File.Exists(transcriptFile))
            p.Transcript = JsonSerializer.Deserialize<List<TranscriptSegment>>(await File.ReadAllTextAsync(transcriptFile, ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (p.SourceKind == SourceKind.YouTube && (p.Transcript.Count == 0 || p.Duration <= 0 || p.Name == "Vídeo do YouTube"))
        {
            await Stage(p, ProjectStatus.Acquiring, 8, "Consultando duração e legendas do YouTube");
            var metadata = await ProbeYouTubeMetadata(p, ct); p.Duration = metadata.Duration;
            if (p.Name == "Vídeo do YouTube" && !string.IsNullOrWhiteSpace(metadata.Title)) p.Name = metadata.Title;
            if (p.Transcript.Count == 0) await TryLoadYouTubeCaptionsAsync(p, allowAutomatic: true, ct);
        }
        var existingMedia = !string.IsNullOrWhiteSpace(p.LocalMedia) ? Path.Combine(dir, p.LocalMedia) : null;
        if (p.SourceKind == SourceKind.YouTube && (existingMedia is null || !File.Exists(existingMedia)))
        {
            storageCapacity.Ensure(StorageOperation.Acquisition, p.Duration);
            var template = Path.Combine(dir, "source.%(ext)s");
            await Stage(p, ProjectStatus.Acquiring, 12, p.Transcript.Count > 0 ? "Baixando vídeo; legendas já aproveitadas" : "Baixando vídeo em formato otimizado");
            var youtubeArgs = YouTubeAcquisition.WithBrowserSession(tools.YouTubeArguments(), p.YouTubeCookieBrowser);
            var downloadArgs = YouTubeAcquisition.DownloadArguments(youtubeArgs, tools.Find("ffmpeg"), template, p.Source);
            try { await tools.RunAsync(tools.Find("yt-dlp"), downloadArgs, dir, ct); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase))
            {
                await Stage(p, ProjectStatus.Acquiring, 14, "YouTube recusou o 1080p; tentando formato compatível");
                var compatibleArgs = YouTubeAcquisition.CompatibleDownloadArguments(youtubeArgs, tools.Find("ffmpeg"), template, p.Source);
                await tools.RunAsync(tools.Find("yt-dlp"), compatibleArgs, dir, ct);
            }
            p.LocalMedia = Path.GetFileName(Directory.EnumerateFiles(dir, "source.*").First(f => Path.GetFileName(f) != "source.audio.wav" && !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)));
            await Checkpoint(p, "media", "Mídia adquirida");
        }
        var media = Path.Combine(dir, p.LocalMedia ?? throw new InvalidOperationException("Arquivo de origem não encontrado."));
        if (!File.Exists(media)) throw new InvalidOperationException("O arquivo de origem não está mais disponível.");
        if (!p.CompletedStages.Contains("media")) await Checkpoint(p, "media", "Mídia disponível");
        if (p.Duration <= 0) p.Duration = await ProbeDuration(media, ct);
        var transcriptReady = p.Transcript.Count > 0;
        var hasManualCaptions = transcriptReady;
        if (!transcriptReady && p.SourceKind == SourceKind.YouTube)
        {
            await Stage(p, ProjectStatus.Transcribing, 18, "Verificando legendas existentes no YouTube");
            hasManualCaptions = await TryLoadYouTubeCaptionsAsync(p, allowAutomatic: false, ct);
        }
        if (!hasManualCaptions && !transcriptReady)
        {
            storageCapacity.Ensure(StorageOperation.Transcription, p.Duration);
            await Stage(p, ProjectStatus.Transcribing, 20, "Extraindo áudio");
            var audio = Path.Combine(dir, "source.audio.wav");
            if (!File.Exists(audio) || new FileInfo(audio).Length < 1024)
            {
                await tools.RunAsync(tools.Find("ffmpeg"), ["-y", "-i", media, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", audio], dir, ct);
                await Checkpoint(p, "audio", "Áudio extraído");
            }
            var worshipMode = p.Options.ContentType == "louvor";
            await Stage(p, ProjectStatus.Transcribing, 35, worshipMode ? "Transcrevendo canto em modo para louvor" : "Transcrevendo com IA local");
            using (await workLimiter.EnterAsync(ProductionWorkKind.Transcription, ct))
                await tools.RunAsync(tools.Find("python"), [Path.Combine(tools.Root, "scripts", "transcribe.py"), audio, transcriptFile, p.Options.WhisperModel, p.Options.ContentType], dir, ct);
            p.Transcript = JsonSerializer.Deserialize<List<TranscriptSegment>>(await File.ReadAllTextAsync(transcriptFile, ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            p.TranscriptSource = $"Faster-Whisper ({p.Options.WhisperModel})";
            if (p.SourceKind == SourceKind.YouTube && (p.Options.ContentType == "louvor" || p.Transcript.Sum(x => x.Text.Length) < 120))
                await TryLoadYouTubeCaptionsAsync(p, allowAutomatic: true, ct);
        }
        if (p.Transcript.Count == 0)
            throw new InvalidOperationException(p.Options.ContentType == "louvor"
                ? "Não foi possível reconhecer voz ou canto neste áudio. Tente o modelo Small ou Medium para melhorar o reconhecimento musical."
                : "Não foi possível reconhecer fala neste áudio, mesmo após a tentativa sem filtro de voz.");
        await Checkpoint(p, "transcript", "Transcrição disponível");
        await Stage(p, ProjectStatus.Analyzing, 72, "Encontrando momentos de impacto");
        p.Options.ApplyAutomaticDuration();
        var analysis = editorial.AnalyzeWithReport(p.Transcript, p.Options);
        p.Clips = analysis.Clips; p.CandidateAnalysis = analysis.Report;
        foreach (var clip in p.Clips)
            clip.BrandTheme = "amado-jesus";
        await Checkpoint(p, "analysis", $"{p.Clips.Count} candidatos encontrados");
        await Stage(p, ProjectStatus.Analyzing, 82, $"Preparando {p.Clips.Count} candidatos");
        await Parallel.ForEachAsync(p.Clips, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (clip, token) =>
        {
            using (await workLimiter.EnterAsync(ProductionWorkKind.Metadata, token))
                await ShortFormMetadataService.EnrichAsync(http, clip, p.Options.ContentType, token);
            await CreateCoverAsync(p, clip, token);
        });
        ShortFormMetadataService.EnsureUniqueTitles(p.Clips, p.Options.ContentType);
        p.Status = ProjectStatus.Ready; p.Progress = 100; p.CompletedAt = DateTime.UtcNow; p.Stage = $"{p.Clips.Count} cortes prontos para revisar"; await store.SaveAsync(p);
    }

    private async Task Stage(VideoProject p, ProjectStatus status, int progress, string stage) { p.Status = status; p.Progress = progress; p.Stage = stage; await store.SaveAsync(p); }
    private async Task Checkpoint(VideoProject p, string stage, string label) { if (!p.CompletedStages.Contains(stage)) p.CompletedStages.Add(stage); p.LastCheckpoint = stage; p.Stage = label; await store.SaveAsync(p); }

    public async Task ResetFromAsync(VideoProject project, string stage)
    {
        var order = new[] { "media", "audio", "transcript", "analysis" };
        var index = Array.IndexOf(order, stage); if (index < 0) throw new InvalidOperationException("Etapa inválida.");
        var dir = store.ProjectDirectory(project.Id);
        project.CompletedStages.RemoveAll(value => Array.IndexOf(order, value) >= index);
        if (index <= 3) { project.Clips = []; foreach (var file in Directory.EnumerateFiles(dir, "cover-*.jpg").Concat(Directory.EnumerateFiles(dir, "clip-*.mp4")).Concat(Directory.EnumerateFiles(dir, "captions-*.ass"))) File.Delete(file); }
        if (index <= 2) { project.Transcript = []; project.TranscriptSource = null; var transcript = Path.Combine(dir, "transcript.json"); if (File.Exists(transcript)) File.Delete(transcript); }
        if (index <= 1) { var audio = Path.Combine(dir, "source.audio.wav"); if (File.Exists(audio)) File.Delete(audio); }
        if (index == 0 && project.SourceKind == SourceKind.YouTube) { foreach (var file in Directory.EnumerateFiles(dir, "source.*").Where(file => !file.EndsWith("source.audio.wav"))) File.Delete(file); project.LocalMedia = null; }
        project.LastCheckpoint = project.CompletedStages.LastOrDefault(); project.Status = ProjectStatus.Queued; project.Progress = 0; project.Error = null; project.Stage = $"Reprocessamento iniciado em {stage}"; await store.SaveAsync(project);
    }
    private async Task<double> ProbeDuration(string media, CancellationToken ct)
    {
        var output = await tools.CaptureAsync(tools.Find("ffprobe"), ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", media], Path.GetDirectoryName(media), ct);
        return double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? duration : 0;
    }
    private async Task<(double Duration, string? Title)> ProbeYouTubeMetadata(VideoProject project, CancellationToken ct)
    {
        try
        {
            var common = YouTubeAcquisition.WithBrowserSession(tools.YouTubeArguments(), project.YouTubeCookieBrowser);
            var output = await tools.CaptureAsync(tools.Find("yt-dlp"), YouTubeAcquisition.MetadataArguments(common, project.Source), tools.Root, ct);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var duration = lines.Length > 0 && double.TryParse(lines[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            return (duration, lines.Length > 1 ? lines[1] : null);
        }
        catch { return (0, null); }
    }

    private static List<ClipCandidate> BuildCandidates(List<TranscriptSegment> segments, ProjectOptions options)
    {
        if (options.ContentType == "louvor") return BuildMusicCandidates(segments, options);
        var candidates = new List<(ClipCandidate Clip, double Rank)>();
        var strongHooks = new[] { "olha deixa eu", "deixa eu te", "posso te falar", "eu vou repetir", "vou falar mais uma vez", "presta atenção", "o que que", "você tem noção", "imagina isso", "a verdade é", "sabe por que", "quando você" };
        var impact = new[] { "deus", "jesus", "coração", "fé", "verdade", "justiça", "amor", "paz", "perdão", "reino", "caos", "serpente", "cruz", "propósito" };
        var weakOpenings = new[] { "agora vamos", "vamos continuar", "continuar aqui", "próximas características", "quem tá entendendo", "beleza", "então gente" };
        for (var anchor = 0; anchor < segments.Count; anchor++)
        {
            var anchorText = segments[anchor].Text.ToLowerInvariant();
            var hookScore = strongHooks.Count(anchorText.Contains) * 18 + impact.Count(anchorText.Contains) * 3 + (anchorText.Contains('?') ? 7 : 0);
            if (hookScore < 6) continue;
            var startIndex = Math.Max(0, anchor - (hookScore >= 18 ? 0 : 1));
            var start = segments[startIndex].Start; var parts = new List<TranscriptSegment>(); var j = startIndex;
            while (j < segments.Count && segments[j].End - start <= options.MaxDuration)
            {
                parts.Add(segments[j]); j++;
                var elapsed = parts[^1].End - start;
                if (elapsed >= options.MinDuration && elapsed >= options.MaxDuration * .72 && EndsThought(parts[^1].Text)) break;
            }
            var duration = parts.LastOrDefault()?.End - start ?? 0; if (duration < options.MinDuration || parts.Count < 3) continue;
            var text = string.Join(" ", parts.Select(x => x.Text)).Trim();
            var lower = text.ToLowerInvariant();
            var rank = 45 + hookScore + impact.Count(lower.Contains) * 2 + CountContrast(lower) * 6 + (EndsThought(parts[^1].Text) ? 8 : 0);
            if (weakOpenings.Any(w => lower.StartsWith(w))) rank -= 24;
            if (text.Length < 240) rank -= 12;
            var title = MakeTitle(text); var cover = MakeCoverText(text);
            candidates.Add((new ClipCandidate { Start = start, End = parts[^1].End, Score = Math.Round(Math.Min(99d, rank), 1), Transcript = text, Title = title, CoverText = cover, Caption = $"{title}. Uma reflexão para guardar e compartilhar. ✨" }, rank));
        }
        var selected = new List<ClipCandidate>();
        foreach (var item in candidates.OrderByDescending(x => x.Rank))
        {
            if (selected.Any(c => Overlap(c.Start, c.End, item.Clip.Start, item.Clip.End) > .28)) continue;
            selected.Add(item.Clip); if (selected.Count == options.ClipCount) break;
        }
        return selected.OrderBy(c => c.Start).ToList();
    }

    private static List<ClipCandidate> BuildMusicCandidates(List<TranscriptSegment> segments, ProjectOptions options)
    {
        var usable = segments.Where(s => s.Text.Replace("[música]", "", StringComparison.OrdinalIgnoreCase).Trim().Length > 2).ToList();
        var pool = new List<ClipCandidate>();
        for (var i = 0; i < usable.Count; i += 2)
        {
            var start = usable[i].Start; if (start < 8) continue;
            var parts = usable.Skip(i).TakeWhile(s => s.End - start <= options.MaxDuration).ToList();
            if (parts.Count < 3 || parts[^1].End - start < options.MinDuration) continue;
            var text = string.Join(" ", parts.Select(s => s.Text.Replace("[música]", "", StringComparison.OrdinalIgnoreCase))).Trim();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var meaningful = words.Count(w => w.Length > 3);
            var score = Math.Min(96, 55 + meaningful / 3d + (text.Contains("Deus", StringComparison.OrdinalIgnoreCase) ? 6 : 0) + (text.Contains("promessa", StringComparison.OrdinalIgnoreCase) ? 8 : 0));
            var worshipTitle = ShortFormMetadataService.GenerateTitleSuggestions(new ClipCandidate { Transcript = text }, "louvor").FirstOrDefault() ?? MakeTitle(text);
            pool.Add(new ClipCandidate { Start = start, End = parts[^1].End, Score = Math.Round(score, 1), Transcript = text, Title = worshipTitle, CoverText = ShortFormMetadataService.NormalizeCoverText(worshipTitle), Caption = $"{worshipTitle}. Uma canção para renovar a fé. 🎶✨", Hashtags = ["#louvor", "#adoração", "#promessa", "#fé", "#worship"] });
        }
        var selected = new List<ClipCandidate>();
        foreach (var clip in pool.OrderByDescending(c => c.Score))
        {
            if (selected.Any(c => Overlap(c.Start, c.End, clip.Start, clip.End) > .22)) continue;
            selected.Add(clip); if (selected.Count == options.ClipCount) break;
        }
        return selected.OrderBy(c => c.Start).ToList();
    }

    private static bool EndsThought(string text) => text.TrimEnd().EndsWith('.') || text.TrimEnd().EndsWith('?') || text.TrimEnd().EndsWith('!');
    private static int CountContrast(string text) => new[] { " mas ", " porque ", " então ", " porém ", " mesmo em meio", " não é ", " é um " }.Count(text.Contains);
    private static double Overlap(double a1, double a2, double b1, double b2) => Math.Max(0, Math.Min(a2, b2) - Math.Max(a1, b1)) / Math.Min(a2 - a1, b2 - b1);
    private static string MakeTitle(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("coração limpo")) return "O que realmente limpa o coração?";
        if (lower.Contains("tranquilidade") && lower.Contains("paz")) return "Paz não é o mesmo que tranquilidade";
        if (lower.Contains("perdoa") && lower.Contains("jesus")) return "O coração de Jesus na cruz";
        if (lower.Contains("desconfia") && lower.Contains("deus")) return "A desconfiança que suja o coração";
        if (lower.Contains("perseguido") && lower.Contains("justiça")) return "Nem toda perseguição é por justiça";
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(9);
        return string.Join(' ', words).Trim(' ', ',', '.', '?', '!');
    }
    private static string MakeCoverText(string text)
    {
        var title = MakeTitle(text).ToUpperInvariant();
        return string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
    }

    private async Task CreateCoverAsync(VideoProject p, ClipCandidate clip, CancellationToken ct)
    {
        var dir = store.ProjectDirectory(p.Id); var cover = $"cover-{clip.Id}.jpg"; var timestamp = clip.CoverTimestamp ?? clip.Start + Math.Min(3, (clip.End - clip.Start) / 2);
        var textFile = Path.Combine(dir, $"cover-text-{clip.Id}.txt");
        var words = clip.CoverText.Split(' ', StringSplitOptions.RemoveEmptyEntries); var midpoint = Math.Max(1, (int)Math.Ceiling(words.Length / 2d));
        await File.WriteAllTextAsync(textFile, string.Join(' ', words.Take(midpoint)) + (words.Length > midpoint ? "\n" + string.Join(' ', words.Skip(midpoint)) : ""), Encoding.UTF8, ct);
        var escapedText = EscapeFilterPath(textFile); var accent = NormalizeColor(clip.CoverAccent); var y = clip.CoverPosition == "top" ? "260" : clip.CoverPosition == "center" ? "(h-text_h)/2" : "h-text_h-300"; var accentY = clip.CoverPosition == "top" ? "520" : clip.CoverPosition == "center" ? "1120" : "1680";
        var font = File.Exists(@"C:\Windows\Fonts\arialbd.ttf") ? $":fontfile='{EscapeFilterPath(@"C:\Windows\Fonts\arialbd.ttf")}'" : ":font='Arial'";
        var filter = $"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:0:{CropY(clip.CropFocus)},{RenderFilterFactory.CreativeLook(0)},drawbox=x=0:y=0:w=iw:h=ih:color=black@0.20:t=fill,drawtext=textfile='{escapedText}'{font}:fontsize=82:fontcolor=0xF3E8D0:borderw=5:bordercolor=black@0.85:line_spacing=16:x=(w-text_w)/2:y={y},drawbox=x=90:y={accentY}:w=220:h=8:color={accent}:t=fill";
        await tools.RunAsync(tools.Find("ffmpeg"), ["-y", "-ss", F(timestamp), "-i", Path.Combine(dir, p.LocalMedia!), "-frames:v", "1", "-vf", filter, Path.Combine(dir, cover)], dir, ct);
        clip.CoverPath = cover;
    }

    public Task RefreshCoverAsync(VideoProject project, ClipCandidate clip, CancellationToken ct = default) => CreateCoverAsync(project, clip, ct);

    public async Task RenderClipAsync(VideoProject p, ClipCandidate clip, CancellationToken ct = default)
    {
        storageCapacity.Ensure(StorageOperation.BatchRender, clip.End - clip.Start);
        using var renderSlot = await workLimiter.EnterAsync(ProductionWorkKind.Render, ct);
        var dir = store.ProjectDirectory(p.Id);
        var speed = p.Options.ContentType == "louvor" ? 1 : RenderFilterFactory.NormalizePlaybackSpeed(clip.PlaybackSpeed);
        clip.PlaybackSpeed = speed;
        var trimPlan = silenceTrimming.Plan(clip, p.Transcript);
        clip.SilenceTrimPlan = trimPlan;
        var subtitleTrack = SubtitleTrackService.Ensure(clip, p.Transcript);
        string? ass = null;
        if (subtitleTrack.Enabled)
        {
            ass = Path.Combine(dir, $"captions-{clip.Id}.ass");
            await File.WriteAllTextAsync(ass, BuildAss(p.Transcript, clip, trimPlan), Encoding.UTF8, ct);
        }
        var output = $"clip-{clip.Id}.mp4";
        var framing = RenderFilterFactory.Framing(clip);
        var watermarkFile = Path.Combine(dir, $"watermark-{clip.Id}.txt");
        await File.WriteAllTextAsync(watermarkFile, clip.WatermarkText ?? "AJ  |  AMADO JESUS", Encoding.UTF8, ct);
        var watermarkFont = File.Exists(@"C:\Windows\Fonts\arialbd.ttf") ? $":fontfile='{EscapeFilterPath(@"C:\Windows\Fonts\arialbd.ttf")}'" : ":font='Arial'";
        var branding = RenderFilterFactory.Branding(clip, EscapeFilterPath(watermarkFile), watermarkFont);
        var videoAnalysis = await videoEnhancement.AnalyzeAsync(Path.Combine(dir, p.LocalMedia!), clip.Start, clip.End - clip.Start, ct);
        var enhancement = VideoEnhancementService.CreateProfile(videoAnalysis);
        var filter = SilenceTrimmingService.VideoPrefix(trimPlan, clip.Start) + ComposeVideoFilter(enhancement.Filter, framing, ass, branding, RenderFilterFactory.CreativeLook(clip.End - clip.Start), speed);
        logger.LogInformation("[Video] project={ProjectId} clip={ClipId} enhancement={Enhancement} luma={Luma} saturation={Saturation}", p.Id, clip.Id, enhancement.Kind, videoAnalysis.LumaAverage, videoAnalysis.SaturationAverage);
        var audioAnalysis = await audioAnalyzer.AnalyzeAsync(Path.Combine(dir, p.LocalMedia!), clip.Start, clip.End - clip.Start, p.Options.ContentType, ct);
        var audio = AudioFilterFactory.Create(audioAnalysis, trimPlan.FinalDuration, speed);
        var audioFilter = SilenceTrimmingService.AudioPrefix(trimPlan, clip.Start) + audio.Filter;
        logger.LogInformation("[Audio] project={ProjectId} clip={ClipId} profile={Profile} meanDb={MeanDb} peakDb={PeakDb}", p.Id, clip.Id, audio.Profile, audioAnalysis.MeanVolumeDb, audioAnalysis.PeakVolumeDb);
        var encoder = await encoderDetector.DetectAsync(ct);
        try { await RenderWithEncoderAsync(encoder); }
        catch (Exception ex) when (encoder.HardwareAccelerated)
        {
            logger.LogWarning(ex, "[Encoder] {Codec} falhou durante o render; repetindo com libx264", encoder.Codec);
            encoderDetector.Invalidate(); await RenderWithEncoderAsync(HardwareEncoderDetector.Cpu);
        }
        clip.VideoPath = output;
        clip.LastRenderFingerprint = RenderStateService.Fingerprint(clip);
        clip.RenderOutdated = false;
        await qualityGate.ValidateAsync(p, clip, ct);

        async Task RenderWithEncoderAsync(RenderEncoderProfile profile)
        {
            var arguments = new List<string> { "-y", "-ss", F(clip.Start), "-to", F(clip.End), "-i", Path.Combine(dir, p.LocalMedia!), "-vf", filter, "-af", audioFilter, "-c:v", profile.Codec };
            arguments.AddRange(profile.Arguments);
            arguments.AddRange(["-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.2", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", Path.Combine(dir, output)]);
            logger.LogInformation("[Encoder] project={ProjectId} clip={ClipId} codec={Codec}", p.Id, clip.Id, profile.Codec);
            await tools.RunAsync(tools.Find("ffmpeg"), arguments, dir, ct);
        }
    }

    public async Task RenderAllAsync(VideoProject p, CancellationToken ct = default)
    {
        var clips = p.Clips.Where(c => c.Approved).ToList(); var completed = 0;
        if (clips.Count > 0) storageCapacity.Ensure(StorageOperation.BatchRender, clips.Average(clip => clip.End - clip.Start), clips.Count);
        p.IsRendering = true; p.RenderCompleted = 0; p.RenderTotal = clips.Count; await store.SaveAsync(p);
        try
        {
            await Parallel.ForEachAsync(clips, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 2), CancellationToken = ct }, async (clip, token) =>
            {
                await RenderClipAsync(p, clip, token);
                p.RenderCompleted = Interlocked.Increment(ref completed);
                await store.SaveAsync(p);
            });
        }
        finally
        {
            p.IsRendering = false; await store.SaveAsync(p);
        }
    }

    public async Task ReanalyzeAndRenderAsync(VideoProject p, CancellationToken ct = default)
    {
        if (p.Transcript.Count == 0) throw new InvalidOperationException("Este projeto ainda não possui transcrição para reaproveitar.");
        p.Options.ApplyAutomaticDuration();
        p.Status = ProjectStatus.Analyzing; p.Progress = 72; p.Stage = "Refazendo o ranking sem transcrever novamente"; await store.SaveAsync(p);
        var analysis = editorial.AnalyzeWithReport(p.Transcript, p.Options);
        p.Clips = analysis.Clips; p.CandidateAnalysis = analysis.Report;
        foreach (var clip in p.Clips) { clip.BrandTheme = "amado-jesus"; using (await workLimiter.EnterAsync(ProductionWorkKind.Metadata, ct)) await ShortFormMetadataService.EnrichAsync(http, clip, p.Options.ContentType, ct); await CreateCoverAsync(p, clip, ct); }
        ShortFormMetadataService.EnsureUniqueTitles(p.Clips, p.Options.ContentType);
        await RenderAllAsync(p, ct);
        p.Status = ProjectStatus.Ready; p.Progress = 100; p.Stage = $"{p.Clips.Count} novos cortes renderizados"; await store.SaveAsync(p);
    }

    public async Task ReanalyzeAsync(VideoProject p, bool render, CancellationToken ct = default)
    {
        if (p.Transcript.Count == 0) throw new InvalidOperationException("Este projeto ainda não possui transcrição.");
        p.Options.ApplyAutomaticDuration();
        p.Status = ProjectStatus.Analyzing; p.Progress = 78; p.Stage = "Aplicando análise editorial"; await store.SaveAsync(p);
        var analysis = editorial.AnalyzeWithReport(p.Transcript, p.Options);
        p.Clips = analysis.Clips; p.CandidateAnalysis = analysis.Report;
        foreach (var clip in p.Clips) { using (await workLimiter.EnterAsync(ProductionWorkKind.Metadata, ct)) await ShortFormMetadataService.EnrichAsync(http, clip, p.Options.ContentType, ct); await CreateCoverAsync(p, clip, ct); }
        ShortFormMetadataService.EnsureUniqueTitles(p.Clips, p.Options.ContentType);
        if (render) await RenderAllAsync(p, ct);
        p.Status = ProjectStatus.Ready; p.Progress = 100; p.Stage = $"{p.Clips.Count} candidatos editoriais prontos"; await store.SaveAsync(p);
    }

    public async Task RecoverYouTubeCaptionsAndRenderAsync(VideoProject p, CancellationToken ct = default)
    {
        if (p.SourceKind != SourceKind.YouTube) throw new InvalidOperationException("O projeto não veio do YouTube.");
        p.Status = ProjectStatus.Transcribing; p.Progress = 55; p.Stage = "Recuperando legendas do YouTube"; await store.SaveAsync(p);
        if (!await TryLoadYouTubeCaptionsAsync(p, allowAutomatic: true, ct)) throw new InvalidOperationException("O YouTube não disponibilizou legendas automáticas para este vídeo.");
        await ReanalyzeAndRenderAsync(p, ct);
    }

    private async Task<bool> TryLoadYouTubeCaptionsAsync(VideoProject p, bool allowAutomatic, CancellationToken ct)
    {
        var dir = store.ProjectDirectory(p.Id); var template = Path.Combine(dir, "youtube-captions");
        try
        {
            foreach (var old in Directory.EnumerateFiles(dir, "youtube-captions*.json3")) File.Delete(old);
            var args = YouTubeAcquisition.WithBrowserSession(tools.YouTubeArguments(), p.YouTubeCookieBrowser);
            args.AddRange(["--skip-download", "--write-subs"]);
            if (allowAutomatic) args.Add("--write-auto-subs");
            args.AddRange(["--sub-langs", "pt-BR,pt-PT,pt-orig,pt", "--sub-format", "json3", "--no-playlist", "-o", template, p.Source]);
            await tools.RunAsync(tools.Find("yt-dlp"), args, dir, ct);
            var file = Directory.EnumerateFiles(dir, "youtube-captions*.json3").OrderBy(f => f.Contains("pt-orig") ? 0 : 1).FirstOrDefault();
            if (file is null) return false;
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct)); var parsed = new List<TranscriptSegment>();
            foreach (var ev in doc.RootElement.GetProperty("events").EnumerateArray())
            {
                if (!ev.TryGetProperty("segs", out var segs) || !ev.TryGetProperty("tStartMs", out var startNode)) continue;
                var text = string.Concat(segs.EnumerateArray().Select(s => s.TryGetProperty("utf8", out var value) ? value.GetString() : "")).Replace("\n", " ").Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var start = startNode.GetDouble() / 1000d; var duration = ev.TryGetProperty("dDurationMs", out var durationNode) ? durationNode.GetDouble() / 1000d : 2d;
                parsed.Add(new TranscriptSegment { Start = start, End = start + Math.Max(.2, duration), Text = text });
            }
            var spokenCharacters = parsed.Sum(s => s.Text.Replace("[música]", "", StringComparison.OrdinalIgnoreCase).Trim().Length);
            var coveredSeconds = parsed.Sum(s => Math.Max(0, s.End - s.Start));
            if (parsed.Count < 3 || spokenCharacters < 80 || (p.Duration > 0 && coveredSeconds < Math.Min(20, p.Duration * .03))) return false;
            p.Transcript = parsed;
            p.TranscriptSource = allowAutomatic ? "Legendas do YouTube (manual ou automática)" : "Legendas manuais do YouTube";
            await File.WriteAllTextAsync(Path.Combine(dir, "transcript.json"), JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true }), ct); await store.SaveAsync(p); return true;
        }
        catch { return false; }
    }

    public static string BuildAss(List<TranscriptSegment> segments, ClipCandidate clip, SilenceTrimPlan? trimPlan = null)
    {
        var playbackSpeed = RenderFilterFactory.NormalizePlaybackSpeed(clip.PlaybackSpeed);
        var (width, height) = RenderFilterFactory.Dimensions(clip.OutputPreset);
        var style = SubtitleFormatter.Style(clip, width, height);
        var sb = new StringBuilder($"[Script Info]\nScriptType: v4.00+\nPlayResX: {width}\nPlayResY: {height}\nWrapStyle: 2\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\n{style}\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n");
        var track = SubtitleTrackService.Ensure(clip, segments);
        if (track.Blocks.Count > 0)
        {
            foreach (var block in track.Blocks.Where(block => block.Enabled && !string.IsNullOrWhiteSpace(block.Text)))
            {
                var timing = SubtitleTrackService.EffectiveTiming(block, track, clip.End - clip.Start);
                if (timing is null) continue;
                sb.AppendLine($"Dialogue: 0,{AssTime(Adjusted(timing.Value.Start))},{AssTime(Adjusted(timing.Value.End))},Impacto,,0,0,0,,{SubtitleFormatter.Plain(block.Text, width)}");
            }
            return sb.ToString();
        }
        var words = segments.SelectMany(s => s.Words).Where(w => w.End >= clip.Start && w.Start <= clip.End).OrderBy(w => w.Start).ToList();
        if (!string.IsNullOrWhiteSpace(clip.EditedTranscript) && words.Count > 0)
        {
            var edited = clip.EditedTranscript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            words = edited.Select((text, index) =>
            {
                var position = edited.Length == 1 ? 0 : index / (double)(edited.Length - 1) * (words.Count - 1);
                var timing = words[(int)Math.Round(position)];
                return new TranscriptWord { Start = timing.Start, End = timing.End, Word = text };
            }).ToList();
        }
        if (words.Count > 0)
        {
            foreach (var group in SubtitleFormatter.SemanticUnits(words))
            {
                var start = Math.Max(0, group[0].Start - clip.Start); var end = Math.Min(clip.End - clip.Start, group[^1].End - clip.Start + .08);
                var karaoke = SubtitleFormatter.Karaoke(group, clip, width);
                sb.AppendLine($"Dialogue: 0,{AssTime(Adjusted(start))},{AssTime(Adjusted(end))},Impacto,,0,0,0,,{karaoke}");
            }
        }
        else foreach (var s in segments.Where(s => s.End >= clip.Start && s.Start <= clip.End))
            sb.AppendLine($"Dialogue: 0,{AssTime(Adjusted(Math.Max(0, s.Start - clip.Start)))},{AssTime(Adjusted(Math.Min(clip.End - clip.Start, s.End - clip.Start)))},Impacto,,0,0,0,,{SubtitleFormatter.Plain(s.Text, width)}");
        return sb.ToString();

        double Adjusted(double relative)
        {
            var absolute = clip.Start + relative;
            var adjusted = trimPlan?.Applied == true ? SilenceTrimmingService.AdjustTime(absolute, trimPlan) : absolute;
            return Math.Max(0, adjusted - clip.Start) / playbackSpeed;
        }
    }
    public static string ComposeVideoFilter(string enhancement, string framing, string? subtitleFile, string? branding = null, string? creativeLook = null, double playbackSpeed = 1)
    {
        var playback = RenderFilterFactory.NormalizePlaybackSpeed(playbackSpeed) > 1 ? $"setpts=PTS/{F(playbackSpeed)}" : null;
        var filter = string.Join(',', new[] { enhancement, framing, creativeLook, branding, playback }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(subtitleFile)) return filter;
        return $"{filter},subtitles='{EscapeFilterPath(subtitleFile)}'";
    }
    private static string EscapeAss(string value) => value.Replace("\n", " ").Replace("{", "(").Replace("}", ")");
    private static string EscapeFilterPath(string value) => value.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    private static string CropY(string focus) => RenderFilterFactory.CropY(focus);
    private static string NormalizeColor(string? value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$") ? "0x" + value![1..] : "0xC7A35A";
    private static string AssTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss\.ff");
    private static string F(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);
}
