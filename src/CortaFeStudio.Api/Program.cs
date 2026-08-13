using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20L * 1024 * 1024 * 1024);
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<ToolService>();
builder.Services.AddSingleton<MediaPipeline>();
builder.Services.AddSingleton<EditorialAnalyzer>();
builder.Services.AddSingleton<ProjectQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProjectQueue>());
builder.Services.AddHttpClient();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "storage", "keys")))
    .SetApplicationName("CortaFeStudio");
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<DiagnosticsService>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<FramingService>();
builder.Services.AddHostedService<PublicationScheduler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapGet("/health", async (ToolService tools) => new { status = "ok", tools = await tools.CheckAsync() });
api.MapGet("/diagnostics", async (DiagnosticsService diagnostics) => await diagnostics.SnapshotAsync());
api.MapGet("/projects", (ProjectStore store) => store.List());
api.MapGet("/queue", (ProjectQueue queue) => queue.Status());
api.MapGet("/storage", (StorageService storage) => storage.Report());
api.MapGet("/projects/{id}", (string id, ProjectStore store) =>
    store.Get(id) is { } project ? Results.Ok(project) : Results.NotFound());

api.MapPost("/projects/url", async (UrlProjectRequest request, ProjectStore store, ProjectQueue queue) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
        !new[] { "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com" }.Contains(uri.Host.ToLowerInvariant()))
        return Results.BadRequest(new { error = "Informe um link válido do YouTube." });
    var project = await store.CreateAsync(request.Name, SourceKind.YouTube, request.Url, request.Options);
    await queue.EnqueueAsync(project.Id);
    return Results.Accepted($"/api/projects/{project.Id}", project);
});

api.MapPost("/projects/upload", async (HttpRequest request, ProjectStore store, ProjectQueue queue) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Envie um formulário com um arquivo." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Selecione um arquivo válido." });
    var options = ProjectOptions.FromForm(form);
    var project = await store.CreateFromUploadAsync(form["name"].FirstOrDefault(), file, options);
    await queue.EnqueueAsync(project.Id);
    return Results.Accepted($"/api/projects/{project.Id}", project);
}).DisableAntiforgery();

api.MapPost("/projects/{id}/retry", async (string id, ProjectStore store, ProjectQueue queue) =>
{
    if (store.Get(id) is null) return Results.NotFound();
    await store.UpdateAsync(id, p => { p.Status = ProjectStatus.Queued; p.Error = null; p.Progress = 0; });
    await queue.EnqueueAsync(id);
    return Results.Accepted();
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
        clip.Start = Math.Max(0, update.Start ?? clip.Start);
        clip.End = Math.Max(clip.Start + 1, update.End ?? clip.End);
        clip.Title = update.Title ?? clip.Title;
        clip.Caption = update.Caption ?? clip.Caption;
        clip.CoverText = update.CoverText ?? clip.CoverText;
        clip.Approved = update.Approved ?? clip.Approved;
        clip.CropFocus = update.CropFocus ?? clip.CropFocus;
        clip.SubtitleStyle = update.SubtitleStyle ?? clip.SubtitleStyle;
        clip.CoverAccent = update.CoverAccent ?? clip.CoverAccent;
        clip.CoverPosition = update.CoverPosition ?? clip.CoverPosition;
        clip.CoverTimestamp = update.CoverTimestamp ?? clip.CoverTimestamp;
        clip.EditedTranscript = update.EditedTranscript?.Trim() ?? clip.EditedTranscript;
        clip.CropX = Math.Clamp(update.CropX ?? clip.CropX, 0, 1);
        clip.LayoutMode = update.LayoutMode ?? clip.LayoutMode;
    });
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
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

api.MapPost("/projects/{id}/clips/{clipId}/render", async (string id, string clipId, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id);
    var clip = project?.Clips.FirstOrDefault(c => c.Id == clipId);
    if (project is null || clip is null) return Results.NotFound();
    try { await pipeline.RenderClipAsync(project, clip); await store.SaveAsync(project); return Results.Ok(clip); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
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
api.MapPost("/projects/{id}/clips/{clipId}/feedback", async (string id, string clipId, ClipFeedbackRequest request, ProjectStore store) =>
{
    if (request.Feedback is not ("approved" or "rejected")) return Results.BadRequest(new { error = "Feedback inválido." });
    var updated = await store.UpdateAsync(id, p => { var clip = p.Clips.FirstOrDefault(c => c.Id == clipId); if (clip is not null) { clip.Feedback = request.Feedback; clip.Approved = request.Feedback == "approved"; } });
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

api.MapPost("/projects/{id}/render-all", async (string id, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await pipeline.RenderAllAsync(project); return Results.Ok(project.Clips); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

api.MapPost("/projects/{id}/recover-youtube-captions", async (string id, ProjectStore store, MediaPipeline pipeline) =>
{
    var project = store.Get(id); if (project is null) return Results.NotFound();
    try { await pipeline.RecoverYouTubeCaptionsAndRenderAsync(project); return Results.Ok(project); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

api.MapGet("/projects/{id}/assets/{**path}", (string id, string path, ProjectStore store) =>
{
    var file = store.ResolveAsset(id, path);
    return file is null ? Results.NotFound() : Results.File(file, GetContentType(file), enableRangeProcessing: true);
});

api.MapGet("/social/status", (SocialService social) => social.Status());
api.MapGet("/social/history", (SocialService social) => social.History());
api.MapPost("/social/publications/{id}/retry", async (string id, SocialService social) =>
{
    try { return Results.Ok(await social.RetryAsync(id)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/social/configure", async (SocialConfigurationRequest request, SocialService social) =>
{ await social.ConfigureAsync(request); return Results.Ok(); });
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
