using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20L * 1024 * 1024 * 1024);
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<ToolService>();
builder.Services.AddSingleton<ToolUpdateService>();
builder.Services.AddSingleton<AudioAnalyzer>();
builder.Services.AddSingleton<VideoEnhancementService>();
builder.Services.AddSingleton<HardwareEncoderDetector>();
builder.Services.AddSingleton<ClipVariantService>();
builder.Services.AddSingleton<QualityGateService>();
builder.Services.AddSingleton<MediaPipeline>();
builder.Services.AddSingleton<RenderJobService>();
builder.Services.AddSingleton<EditorialScoringService>();
builder.Services.AddSingleton<EditorialCandidateSelector>();
builder.Services.AddSingleton<EditorialAnalyzer>();
builder.Services.AddSingleton<LongVideoEditorialAnalyzer>();
builder.Services.AddSingleton<EditorialLearningService>();
builder.Services.AddSingleton<PerformanceLearningService>();
builder.Services.AddSingleton<ProjectQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProjectQueue>());
builder.Services.AddHttpClient();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "storage", "keys")))
    .SetApplicationName("CortaFeStudio");
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<ContentCalendarService>();
builder.Services.AddSingleton<ProductionWorkLimiter>();
builder.Services.AddSingleton<DiagnosticsService>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<StorageCapacityService>();
builder.Services.AddSingleton<ProjectRetentionService>();
builder.Services.AddHostedService<ProjectRetentionWorker>();
builder.Services.AddSingleton<SilenceTrimmingService>();
builder.Services.AddSingleton<FramingService>();
builder.Services.AddSingleton<ManualClipService>();
builder.Services.AddSingleton<WaveformService>();
builder.Services.AddSingleton<ClipSubtitleTranscriptionService>();
builder.Services.AddSingleton<ClipExportService>();
builder.Services.AddSingleton<LocalSecurityService>();
builder.Services.AddHostedService<PublicationScheduler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.Loopback)) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "O Amado Jesus Studio aceita acesso somente deste computador." }); return; }
    var security = context.RequestServices.GetRequiredService<LocalSecurityService>(); var path = context.Request.Path;
    if (security.Enabled && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/security") && !security.ValidSession(context.Request.Cookies["cortafe-session"])) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Sessão local expirada." }); return; }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) || context.File.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    }
});
app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.svg", permanent: true));

var api = app.MapGroup("/api");
api.MapGet("/security/status", (LocalSecurityService security) => new { enabled = security.Enabled });
api.MapPost("/security/configure", async (PinRequest request, LocalSecurityService security) => { await security.ConfigurePinAsync(request.Pin); return Results.Ok(); });
api.MapPost("/security/login", (PinRequest request, HttpResponse response, LocalSecurityService security) => { if (!security.VerifyPin(request.Pin)) return Results.BadRequest(new { error = "PIN incorreto." }); response.Cookies.Append("cortafe-session", security.CreateSession(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, MaxAge = TimeSpan.FromHours(12) }); return Results.Ok(); });
api.MapPost("/security/backup", async (BackupRequest request, LocalSecurityService security) => { try { return Results.Ok(new { path = await security.CreateBackupAsync(request.Password) }); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); } });
api.MapGet("/health", async (ToolService tools) => new { status = "ok", tools = await tools.CheckAsync() });
api.MapGet("/diagnostics", async (DiagnosticsService diagnostics) => await diagnostics.SnapshotAsync());
api.MapGet("/tools/updates", async (ToolUpdateService updates, CancellationToken ct) => await updates.CheckAsync(ct));
api.MapPost("/tools/yt-dlp/update", async (ToolUpdateService updates, CancellationToken ct) =>
{
    try { return Results.Ok(await updates.UpdateYtDlpAsync(ct)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapGet("/projects", (ProjectStore store) => store.List());
api.MapPut("/projects/{id}/library", async (string id, LibraryProjectUpdate request, ProjectStore store) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound(); project.Favorite = request.Favorite ?? project.Favorite; project.Pinned = request.Pinned ?? project.Pinned; await store.SaveAsync(project); return Results.Ok(project);
});
api.MapGet("/queue", (ProjectQueue queue) => queue.Status());
api.MapGet("/failures", (ProjectStore store) => store.ListAll()
    .Where(project => project.Status == ProjectStatus.Failed || project.FailureHistory.Count > 0)
    .Select(project => new { project.Id, project.Name, project.Status, project.Stage, project.Error, project.FailureCode, project.Attempt, project.NextRetryAt, project.LastCheckpoint, failures = project.FailureHistory.OrderByDescending(item => item.At) })
    .OrderByDescending(project => project.failures.FirstOrDefault()?.At ?? DateTime.MinValue));
api.MapGet("/storage", (StorageService storage) => storage.Report());
api.MapGet("/storage/capacity", (StorageOperation operation, double durationSeconds, int itemCount, StorageCapacityService storage) => storage.Check(operation, durationSeconds, itemCount));
api.MapGet("/storage/new-project-capacity", (int itemCount, long uploadBytes, StorageCapacityService storage) => storage.CheckNewProject(itemCount, uploadBytes));
api.MapPost("/storage/temporary-cleanup", async (StorageCapacityService storage) => Results.Ok(new { freedBytes = await storage.CleanupTemporaryAsync() }));
api.MapGet("/storage/retention", (ProjectRetentionService retention) => retention.GetPolicy());
api.MapGet("/storage/retention/preview", (ProjectRetentionService retention) => retention.Preview());
api.MapPut("/storage/retention", async (RetentionPolicyUpdate request, ProjectRetentionService retention, CancellationToken ct) =>
{
    try { return Results.Ok(await retention.UpdateAsync(request, ct)); }
    catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/storage/retention/run", async (ProjectRetentionService retention, CancellationToken ct) => Results.Ok(await retention.ExecuteAsync(force: true, ct)));
api.MapGet("/render/encoder", async (HardwareEncoderDetector detector, CancellationToken ct) => await detector.DetectAsync(ct));
api.MapGet("/projects/{projectId}/clips/{clipId}/quality", async (string projectId, string clipId, ProjectStore store, QualityGateService quality, CancellationToken ct) =>
{
    var project = store.Get(projectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    return project is null || clip is null ? Results.NotFound() : Results.Ok(await quality.ValidateAsync(project, clip, ct));
});
api.MapPost("/projects/{projectId}/clips/{clipId}/quality/repair", async (string projectId, string clipId, ProjectStore store, MediaPipeline pipeline, QualityGateService quality, CancellationToken ct) =>
{
    var project = store.Get(projectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    await pipeline.RenderClipAsync(project, clip, ct); return Results.Ok(await quality.ValidateAsync(project, clip, ct));
});
api.MapGet("/projects/{id}", (string id, ProjectStore store) =>
    store.Get(id) is { } project ? Results.Ok(project) : Results.NotFound());
api.MapGet("/projects/{projectId}/clips/{clipId}/metadata", (string projectId, string clipId, ProjectStore store) =>
{
    var project = store.Get(projectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    return project is null || clip is null ? Results.NotFound() : Results.Ok(clip.PlatformMetadata);
});
api.MapPost("/projects/{projectId}/clips/{clipId}/metadata/regenerate", async (string projectId, string clipId, ProjectStore store, IHttpClientFactory http, CancellationToken ct) =>
{
    var project = store.Get(projectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId); if (project is null || clip is null) return Results.NotFound();
    await ShortFormMetadataService.EnrichAsync(http, clip, project.Options.ContentType, ct); await store.SaveAsync(project); return Results.Ok(clip.PlatformMetadata);
});
api.MapDelete("/projects/{id}", async (string id, ProjectStore store) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    if (project.Status is not (ProjectStatus.Ready or ProjectStatus.Failed or ProjectStatus.Cancelled))
        return Results.Conflict(new { error = "Cancele o processamento antes de excluir este projeto." });
    return await store.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
});

api.MapPost("/projects/url", async (UrlProjectRequest request, ProjectStore store, ProjectQueue queue, StorageCapacityService capacity) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
        !new[] { "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com" }.Contains(uri.Host.ToLowerInvariant()))
        return Results.BadRequest(new { error = "Informe um link válido do YouTube." });
    try { capacity.EnsureNewProject(); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    var project = await store.CreateAsync(request.Name, SourceKind.YouTube, request.Url, request.Options);
    await queue.EnqueueAsync(project.Id);
    return Results.Accepted($"/api/projects/{project.Id}", project);
});

api.MapPost("/projects/url-batch", async (UrlBatchProjectRequest request, ProjectStore store, ProjectQueue queue, StorageCapacityService capacity) =>
{
    var urls = request.Urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToList();
    if (urls.Count == 0) return Results.BadRequest(new { error = "Informe pelo menos um link do YouTube." });
    if (urls.Count > 20) return Results.BadRequest(new { error = "Envie no máximo 20 links por lote." });
    if (urls.Any(url => !IsYouTubeUrl(url))) return Results.BadRequest(new { error = "O lote contém um link que não pertence ao YouTube." });
    try { capacity.EnsureNewProject(urls.Count); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    var projects = new List<VideoProject>();
    for (var index = 0; index < urls.Count; index++)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : $"{request.Name} · {index + 1:00}";
        var project = await store.CreateAsync(name, SourceKind.YouTube, urls[index], request.Options);
        await queue.EnqueueAsync(project.Id); projects.Add(project);
    }
    return Results.Accepted("/api/projects", projects);
});

api.MapPost("/projects/upload", async (HttpRequest request, ProjectStore store, ProjectQueue queue, StorageCapacityService capacity) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Envie um formulário com um arquivo." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Selecione um arquivo válido." });
    try { capacity.EnsureNewProject(uploadBytes: file.Length); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    var options = ProjectOptions.FromForm(form);
    var project = await store.CreateFromUploadAsync(form["name"].FirstOrDefault(), file, options);
    await queue.EnqueueAsync(project.Id);
    return Results.Accepted($"/api/projects/{project.Id}", project);
}).DisableAntiforgery();

api.MapPost("/projects/{id}/retry", async (string id, HttpRequest http, ProjectStore store, ProjectQueue queue) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    if (project.Status is not (ProjectStatus.Failed or ProjectStatus.Cancelled))
        return Results.Conflict(new { error = "Este projeto já está na fila ou em processamento." });
    try
    {
        var request = http.ContentLength > 0
            ? await http.ReadFromJsonAsync<RetryProjectRequest>() ?? new RetryProjectRequest()
            : new RetryProjectRequest();
        var browser = YouTubeAcquisition.WithBrowserSession([], request.Browser);
        await store.UpdateAsync(id, p =>
        {
            p.YouTubeCookieBrowser = browser.Count == 0 ? null : request.Browser!.Trim().ToLowerInvariant();
            p.Status = ProjectStatus.Queued; p.Error = null; p.FailureCode = null; p.Progress = 1;
            p.Stage = p.YouTubeCookieBrowser is null ? "Retentativa adicionada à fila" : "Aguardando acesso com a sessão do navegador";
        });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await queue.EnqueueAsync(id);
    return Results.Accepted($"/api/projects/{id}", store.Get(id));
});
api.MapPost("/projects/{id}/cancel", async (string id, ProjectStore store, ProjectQueue queue) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    queue.Cancel(id); project.Status = ProjectStatus.Cancelled; project.Stage = "Processamento cancelado"; project.Error = null; await store.SaveAsync(project); return Results.Ok(project);
});

api.MapPost("/projects/{id}/restart-from", async (string id, RestartFromRequest request, ProjectStore store, ProjectQueue queue, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await pipeline.ResetFromAsync(project, request.Stage); await queue.EnqueueAsync(id); return Results.Accepted($"/api/projects/{id}", project); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/projects/{id}/cleanup", async (string id, CleanupProjectRequest request, ProjectStore store, StorageService storage) =>
{
    var project = store.Get(id); return project is null ? Results.NotFound() : Results.Ok(new { freedBytes = await storage.CleanupAsync(project, request.DeleteSource) });
});
api.MapPost("/projects/{id}/delete-data", async (string id, ProjectStore store, StorageService storage) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { return Results.Ok(new { freedBytes = await storage.DeleteProjectDataAsync(project) }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
api.MapPost("/projects/delete-data-batch", async (BatchProjectDataRequest request, ProjectStore store, StorageService storage) =>
{
    long freed = 0; var cleaned = new List<string>();
    foreach (var id in request.ProjectIds.Distinct().Take(50)) { var project = store.Get(id); if (project is null || project.Status is not (ProjectStatus.Ready or ProjectStatus.Failed or ProjectStatus.Cancelled)) continue; freed += await storage.DeleteProjectDataAsync(project); cleaned.Add(id); }
    return Results.Ok(new { freedBytes = freed, projectIds = cleaned });
});
api.MapPost("/projects/{id}/archive", async (string id, ProjectStore store, StorageService storage) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound(); await storage.ArchiveAsync(project, true); return Results.Ok();
});

api.MapPut("/projects/{id}/clips/{clipId}", async (string id, string clipId, ClipUpdate update, ProjectStore store) =>
{
    var updated = await store.UpdateAsync(id, p =>
    {
        var clip = p.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip is null) return;
        var previousFingerprint = RenderStateService.Fingerprint(clip);
        clip.Start = Math.Max(0, update.Start ?? clip.Start);
        clip.End = Math.Max(clip.Start + 1, update.End ?? clip.End);
        if (update.Title is not null && !string.Equals(update.Title.Trim(), clip.Title, StringComparison.Ordinal))
        {
            clip.Title = update.Title.Trim();
            clip.TitleEditedByUser = true;
        }
        clip.Caption = update.Caption ?? clip.Caption;
        clip.CoverText = update.CoverText ?? clip.CoverText;
        clip.Approved = update.Approved ?? clip.Approved;
        clip.CropFocus = update.CropFocus ?? clip.CropFocus;
        clip.SubtitleStyle = update.SubtitleStyle ?? clip.SubtitleStyle;
        clip.CoverAccent = update.CoverAccent ?? clip.CoverAccent;
        clip.CoverPosition = update.CoverPosition ?? clip.CoverPosition;
        clip.CoverTimestamp = update.CoverTimestamp ?? clip.CoverTimestamp;
        clip.EditedTranscript = update.EditedTranscript?.Trim() ?? clip.EditedTranscript;
        if (update.CropX is { } cropX && Math.Abs(cropX - clip.CropX) > .001) clip.FramingTrack.Clear();
        clip.CropX = Math.Clamp(update.CropX ?? clip.CropX, 0, 1);
        clip.LayoutMode = update.LayoutMode ?? clip.LayoutMode;
        clip.SplitLeftX = Math.Clamp(update.SplitLeftX ?? clip.SplitLeftX, 0, 1);
        clip.SplitRightX = Math.Clamp(update.SplitRightX ?? clip.SplitRightX, 0, 1);
        if (update.OutputPreset is "vertical" or "portrait" or "square" or "landscape") clip.OutputPreset = update.OutputPreset;
        clip.BrandFrameEnabled = update.BrandFrameEnabled ?? clip.BrandFrameEnabled;
        clip.BrandTheme = update.BrandTheme ?? clip.BrandTheme;
        clip.WatermarkEnabled = update.WatermarkEnabled ?? clip.WatermarkEnabled;
        clip.WatermarkText = update.WatermarkText?.Trim() ?? clip.WatermarkText;
        clip.WatermarkOpacity = update.WatermarkOpacity is null ? clip.WatermarkOpacity : Math.Clamp(update.WatermarkOpacity.Value, .1, 1);
        clip.PlaybackSpeed = p.Options.ContentType == "louvor" ? 1 : update.PlaybackSpeed is 1.25 or 1.5 ? update.PlaybackSpeed.Value : 1;
        clip.SilenceTrimmingEnabled = update.SilenceTrimmingEnabled ?? clip.SilenceTrimmingEnabled;
        if (update.TransitionStyle is "smooth" or "editorial" or "dynamic") clip.TransitionStyle = update.TransitionStyle;
        RenderStateService.MarkIfChanged(clip, previousFingerprint);
    });
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
api.MapPost("/projects/{id}/clips/manual", async (string id, ManualClipRequest request, ProjectStore store, ManualClipService manualClips) =>
{
    var project = store.Get(id);
    if (project is null) return Results.NotFound();
    try
    {
        var clip = manualClips.Create(project, request.Start, request.End);
        project.Clips.Add(clip);
        await store.SaveAsync(project);
        return Results.Created($"/api/projects/{id}/clips/{clip.Id}", clip);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
api.MapGet("/projects/{id}/clips/{clipId}/subtitles", (string id, string clipId, ProjectStore store) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    return project is null || clip is null ? Results.NotFound() : Results.Ok(SubtitleTrackService.Ensure(clip, project.Transcript));
});
api.MapPut("/projects/{id}/clips/{clipId}/subtitles", async (string id, string clipId, SubtitleTrack request, ProjectStore store) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    try
    {
        var previousFingerprint = RenderStateService.Fingerprint(clip);
        clip.SubtitleTrack = SubtitleTrackService.Validate(request, clip.End - clip.Start);
        clip.SubtitleStyle = clip.SubtitleTrack.Style;
        RenderStateService.MarkIfChanged(clip, previousFingerprint);
        await store.SaveAsync(project);
        return Results.Ok(clip.SubtitleTrack);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/projects/{id}/clips/{clipId}/subtitles/regenerate", async (string id, string clipId, ProjectStore store) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    clip.SubtitleTrack = SubtitleTrackService.Create(clip, project.Transcript);
    await store.SaveAsync(project);
    return Results.Ok(clip.SubtitleTrack);
});
api.MapPost("/projects/{id}/clips/{clipId}/subtitles/from-audio", async (string id, string clipId, ProjectStore store, ClipSubtitleTranscriptionService transcription, CancellationToken ct) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    try
    {
        var previousFingerprint = RenderStateService.Fingerprint(clip);
        clip.SubtitleTrack = await transcription.TranscribeAsync(project, clip, ct);
        clip.SubtitleStyle = clip.SubtitleTrack.Style;
        RenderStateService.MarkIfChanged(clip, previousFingerprint);
        await store.SaveAsync(project);
        return Results.Ok(clip.SubtitleTrack);
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/projects/{id}/clips/{clipId}/title-suggestions", (string id, string clipId, ProjectStore store) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    var existing = project.Clips.Where(item => item.Id != clipId).Select(item => item.Title);
    return Results.Ok(new { suggestions = ShortFormMetadataService.GenerateTitleSuggestions(clip, project.Options.ContentType, existing) });
});
api.MapDelete("/projects/{id}/clips/{clipId}", async (string id, string clipId, ProjectStore store) =>
    await store.DeleteClipAsync(id, clipId) ? Results.NoContent() : Results.NotFound());
api.MapPost("/projects/{id}/clips/{clipId}/analyze-framing", async (string id, string clipId, ProjectStore store, FramingService framing) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId); if (project is null || clip is null) return Results.NotFound();
    try { return Results.Ok(await framing.AnalyzeAsync(project, clip)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

api.MapPost("/projects/{id}/clips/{clipId}/duplicate", async (string id, string clipId, ProjectStore store) =>
{
    ClipCandidate? copy = null;
    var updated = await store.UpdateAsync(id, project =>
    {
        var source = project.Clips.FirstOrDefault(clip => clip.Id == clipId); if (source is null) return;
        copy = JsonSerializer.Deserialize<ClipCandidate>(JsonSerializer.Serialize(source));
        if (copy is null) return;
        copy.Id = Guid.NewGuid().ToString("N")[..10]; copy.Title += " (cópia)"; copy.VideoPath = null; copy.CoverPath = null; copy.Feedback = "pending";
        project.Clips.Insert(project.Clips.IndexOf(source) + 1, copy);
    });
    return updated is null || copy is null ? Results.NotFound() : Results.Ok(copy);
});

api.MapPost("/projects/{id}/clips/{clipId}/split", async (string id, string clipId, SplitClipRequest request, ProjectStore store) =>
{
    List<ClipCandidate>? parts = null;
    var updated = await store.UpdateAsync(id, project =>
    {
        var source = project.Clips.FirstOrDefault(clip => clip.Id == clipId); if (source is null || request.At <= source.Start + 3 || request.At >= source.End - 3) return;
        var left = JsonSerializer.Deserialize<ClipCandidate>(JsonSerializer.Serialize(source))!;
        var right = JsonSerializer.Deserialize<ClipCandidate>(JsonSerializer.Serialize(source))!;
        left.Id = Guid.NewGuid().ToString("N")[..10]; left.End = request.At; left.Title += " · Parte 1"; left.VideoPath = null; left.CoverPath = null;
        right.Id = Guid.NewGuid().ToString("N")[..10]; right.Start = request.At; right.Title += " · Parte 2"; right.VideoPath = null; right.CoverPath = null;
        var index = project.Clips.IndexOf(source); project.Clips.RemoveAt(index); project.Clips.InsertRange(index, [left, right]); parts = [left, right];
    });
    return updated is null || parts is null ? Results.BadRequest(new { error = "Escolha um ponto com pelo menos 3 segundos de cada lado." }) : Results.Ok(parts);
});

api.MapPost("/projects/{id}/clips/{clipId}/cover", async (string id, string clipId, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(c => c.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    try { await pipeline.RefreshCoverAsync(project, clip); await store.SaveAsync(project); return Results.Ok(clip); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

api.MapPost("/projects/{id}/clips/{clipId}/render", async (string id, string clipId, ProjectStore store, RenderJobService jobs) =>
{
    var project = store.Get(id);
    var clip = project?.Clips.FirstOrDefault(c => c.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    try { await jobs.RunClipAsync(project, clip); await store.SaveAsync(project); return Results.Ok(clip); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});
api.MapPost("/projects/{id}/clips/{clipId}/render/cancel", (string id, string clipId, RenderJobService jobs) => jobs.Cancel(id, clipId) ? Results.Ok() : Results.NotFound());
api.MapPost("/projects/{id}/clips/{clipId}/preview", async (string id, string clipId, ProjectStore store, MediaPipeline pipeline, CancellationToken ct) =>
{
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId); if (project is null || clip is null) return Results.NotFound();
    try { return Results.Ok(new { path = await pipeline.RenderPreviewAsync(project, clip, ct) }); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

api.MapPost("/projects/{id}/reanalyze", async (string id, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await pipeline.ReanalyzeAndRenderAsync(project); return Results.Ok(project); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});
api.MapPost("/projects/{id}/editorial-analysis", async (string id, ReanalyzeRequest request, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    project.Options.Topic = request.Topic?.Trim();
    if (request.ClipCount is > 0) project.Options.ClipCount = Math.Clamp(request.ClipCount.Value, 1, 20);
    try { await pipeline.ReanalyzeAsync(project, request.Render); return Results.Ok(project); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/projects/{id}/clips/{clipId}/feedback", async (string id, string clipId, ClipFeedbackRequest request, ProjectStore store, EditorialLearningService learning) =>
{
    if (request.Feedback is not ("approved" or "rejected")) return Results.BadRequest(new { error = "Feedback inválido." });
    ClipCandidate? selected = null; var updated = await store.UpdateAsync(id, p => { selected = p.Clips.FirstOrDefault(c => c.Id == clipId); if (selected is not null) { selected.Feedback = request.Feedback; selected.Approved = request.Feedback == "approved"; } });
    if (updated is not null && selected is not null) await learning.RecordAsync(updated, selected, request.Feedback);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
api.MapPut("/projects/{id}/clips/{clipId}/tiktok-workflow", async (string id, string clipId, TikTokWorkflowUpdate request, ProjectStore store) =>
{
    var allowed = new[] { "draft", "ready", "scheduled", "published", "discarded" }; if (!allowed.Contains(request.Status)) return Results.BadRequest(new { error = "Status do TikTok inválido." });
    var project = store.Get(id); var clip = project?.Clips.FirstOrDefault(item => item.Id == clipId); if (project is null || clip is null) return Results.NotFound();
    clip.TikTokWorkflowStatus = request.Status; clip.TikTokScheduledAt = request.Status == "scheduled" ? request.ScheduledAt : null; clip.TikTokPublishedAt = request.Status == "published" ? DateTimeOffset.Now : clip.TikTokPublishedAt; await store.SaveAsync(project); return Results.Ok(clip);
});
api.MapPost("/projects/{id}/clips/feedback-batch", async (string id, BatchFeedbackRequest request, ProjectStore store, EditorialLearningService learning) =>
{
    if (request.Feedback is not ("approved" or "rejected")) return Results.BadRequest(new { error = "Feedback inválido." });
    var project = store.Get(id); if (project is null) return Results.NotFound();
    var selected = project.Clips.Where(clip => request.ClipIds.Contains(clip.Id)).ToList();
    foreach (var clip in selected) { clip.Feedback = request.Feedback; clip.Approved = request.Feedback == "approved"; await learning.RecordAsync(project, clip, request.Feedback); }
    await store.SaveAsync(project); return Results.Ok(project);
});
api.MapGet("/editorial/profile", (EditorialLearningService learning) => learning.Profile());
api.MapDelete("/editorial/profile", async (EditorialLearningService learning) => { await learning.ResetAsync(); return Results.NoContent(); });
api.MapGet("/performance", (PerformanceLearningService learning) => learning.List());
api.MapGet("/performance/insights", (string? profile, PerformanceLearningService learning) => learning.Insights(profile));
api.MapPost("/performance", async (RecordPerformanceRequest request, ProjectStore store, PerformanceLearningService learning) =>
{
    var project = store.Get(request.ProjectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == request.ClipId); if (project is null || clip is null) return Results.NotFound();
    try { return Results.Ok(await learning.RecordAsync(project, clip, request)); }
    catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

api.MapPost("/projects/{id}/render-all", async (string id, ProjectStore store, RenderJobService jobs) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await jobs.RunBatchAsync(project); return Results.Ok(project.Clips); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});
api.MapPost("/projects/{id}/render-all/cancel", (string id, RenderJobService jobs) => jobs.CancelBatch(id) ? Results.Ok() : Results.NotFound());

api.MapPost("/projects/{id}/recover-youtube-captions", async (string id, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await pipeline.RecoverYouTubeCaptionsAndRenderAsync(project); return Results.Ok(project); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

api.MapGet("/projects/{id}/source", (string id, ProjectStore store) =>
{
    var project = store.Get(id);
    if (project is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(project.LocalMedia))
        return Results.NotFound(new { error = "A mídia original não está disponível." });
    var file = store.ResolveAsset(id, project.LocalMedia);
    return file is null
        ? Results.NotFound(new { error = "A mídia original foi removida do armazenamento." })
        : Results.File(file, GetContentType(file), enableRangeProcessing: true);
});
api.MapGet("/projects/{id}/waveform", async (string id, WaveformService waveform, CancellationToken ct) =>
{
    try { return Results.Ok(new { samples = await waveform.GetAsync(id, ct) }); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException) { return Results.BadRequest(new { error = ex.Message }); }
});

api.MapGet("/projects/{id}/assets/{**path}", (string id, string path, ProjectStore store) =>
{
    var file = store.ResolveAsset(id, path);
    return file is null ? Results.NotFound() : Results.File(file, GetContentType(file), enableRangeProcessing: true);
});
api.MapGet("/projects/{id}/exports/clips.zip", async (string id, ProjectStore store, ClipExportService exports, CancellationToken ct) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { var file = await exports.CreateZipAsync(project, ct); return Results.File(file, "application/zip", $"{project.Name}-cortes.zip", enableRangeProcessing: true); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapGet("/projects/{id}/exports/tiktok-studio.zip", async (string id, ProjectStore store, ClipExportService exports, CancellationToken ct) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { var file = await exports.CreateTikTokStudioPackageAsync(project, ct); return Results.File(file, "application/zip", $"{project.Name}-tiktok-studio.zip", enableRangeProcessing: true); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapGet("/projects/{id}/exports/project.json", (string id, ProjectStore store) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    return Results.File(JsonSerializer.SerializeToUtf8Bytes(project, options), "application/json", $"amado-jesus-{project.Id}.json");
});

api.MapGet("/social/status", (SocialService social) => social.Status());
api.MapGet("/social/history", (SocialService social) => social.History());
api.MapGet("/calendar", (ContentCalendarService calendar) => calendar.List());
api.MapPost("/calendar", async (CreateCalendarRequest request, ProjectStore store, ContentCalendarService calendar) =>
{
    var project = store.Get(request.ProjectId); if (project is null) return Results.NotFound();
    var clips = project.Clips.Where(clip => request.ClipIds.Count == 0 || request.ClipIds.Contains(clip.Id)).Where(clip => clip.Approved && clip.VideoPath is not null).ToList();
    if (clips.Count == 0) return Results.BadRequest(new { error = "Selecione pelo menos um corte aprovado e renderizado." });
    try { return Results.Ok(await calendar.ScheduleAsync(project, clips, request.Platforms, request.Strategy)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPut("/calendar/{id}", async (string id, RescheduleContentRequest request, ContentCalendarService calendar) => { try { return Results.Ok(await calendar.RescheduleAsync(id, request.ScheduledAt)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); } });
api.MapDelete("/calendar/{id}", async (string id, ContentCalendarService calendar) => { try { return Results.Ok(await calendar.CancelAsync(id)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); } });
api.MapPost("/calendar/{id}/publish-now", async (string id, ContentCalendarService calendar) => { try { return Results.Ok(await calendar.PublishNowAsync(id)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); } });
api.MapPost("/calendar/{id}/retry", async (string id, ContentCalendarService calendar) => { try { return Results.Ok(await calendar.RetryAsync(id)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); } });
api.MapPost("/social/publications/{id}/retry", async (string id, SocialService social) =>
{
    try { return Results.Ok(await social.RetryAsync(id)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/social/publications/{id}/refresh", async (string id, SocialService social) =>
{
    try { return Results.Ok(await social.RefreshStatusAsync(id)); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/social/configure", async (SocialConfigurationRequest request, SocialService social) =>
    { await social.ConfigureAsync(request); return Results.Ok(); });
api.MapDelete("/social/accounts/{platform}", async (SocialPlatform platform, SocialService social) => { await social.DisconnectAsync(platform); return Results.NoContent(); });
api.MapGet("/social/connect/{platform}", (SocialPlatform platform, HttpRequest request, SocialService social) =>
{
    try { return Results.Ok(new { url = social.AuthorizationUrl(platform, $"{request.Scheme}://{request.Host}") }); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapGet("/social/callback/{platform}", async (SocialPlatform platform, string? code, string? state, string? error, HttpRequest request, SocialService social) =>
{
    if (!string.IsNullOrWhiteSpace(error)) return Results.Content($"<h1>Conexão cancelada</h1><p>{System.Net.WebUtility.HtmlEncode(error)}</p>", "text/html");
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Results.BadRequest("Código OAuth ausente.");
    try { await social.CompleteOAuthAsync(platform, code, state, $"{request.Scheme}://{request.Host}"); return Results.Content("<script>window.opener?.postMessage('social-connected','*');window.close()</script><h1>Conta conectada.</h1>", "text/html"); }
    catch (Exception ex) { return Results.Content($"<h1>Falha na conexão</h1><pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>", "text/html"); }
});
api.MapPost("/projects/{projectId}/clips/{clipId}/publish", async (string projectId, string clipId, PublishRequest request, SocialService social) =>
{
    try { var result = await social.PublishAsync(projectId, clipId, request); return result.Status == "failed" ? Results.BadRequest(result) : Results.Ok(result); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapFallbackToFile("index.html");
app.Run();

static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".mp4" => "video/mp4", ".webm" => "video/webm", ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png", ".json" => "application/json", _ => "application/octet-stream"
};
static bool IsYouTubeUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
    new[] { "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com" }.Contains(uri.Host.ToLowerInvariant());
public record PinRequest(string Pin);
public record BackupRequest(string Password);

public partial class Program;
