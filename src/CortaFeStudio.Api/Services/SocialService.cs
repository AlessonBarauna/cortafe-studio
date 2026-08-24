using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CortaFeStudio.Api.Services;

public sealed class SocialService
{
    private readonly string _file;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _http;
    private readonly ProjectStore _projects;
    private readonly QualityGateService _quality;
    private readonly Dictionary<SocialPlatform, SocialCredential> _accounts = [];
    private readonly Dictionary<string, PendingAuthorization> _states = [];
    private readonly List<PublicationRecord> _history = [];
    private readonly ProductionWorkLimiter _workLimiter;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SocialService(IWebHostEnvironment env, IDataProtectionProvider dataProtection, IHttpClientFactory http, ProjectStore projects, QualityGateService quality, ProductionWorkLimiter workLimiter)
    {
        var root = Path.Combine(env.ContentRootPath, "storage", "social");
        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "credentials.protected");
        _protector = dataProtection.CreateProtector("CortaFeStudio.Social.v1");
        _http = http;
        _projects = projects;
        _quality = quality;
        _workLimiter = workLimiter;
        Load();
    }

    public object Status() => Enum.GetValues<SocialPlatform>().Select(platform =>
    {
        _accounts.TryGetValue(platform, out var account);
        return new
        {
            platform,
            configured = !string.IsNullOrWhiteSpace(account?.ClientId),
            connected = !string.IsNullOrWhiteSpace(account?.AccessToken),
            accountName = account?.AccountName,
            expiresAt = account?.ExpiresAt,
            publicUrlConfigured = !string.IsNullOrWhiteSpace(account?.PublicBaseUrl)
        };
    });

    public async Task ConfigureAsync(SocialConfigurationRequest request)
    {
        _accounts.TryGetValue(request.Platform, out var existing);
        _accounts[request.Platform] = new SocialCredential
        {
            Platform = request.Platform,
            ClientId = request.ClientId.Trim(),
            ClientSecret = request.ClientSecret.Trim(),
            PublicBaseUrl = request.PublicBaseUrl?.TrimEnd('/'),
            AccessToken = existing?.AccessToken,
            RefreshToken = existing?.RefreshToken,
            ExpiresAt = existing?.ExpiresAt,
            AccountId = existing?.AccountId,
            AccountName = existing?.AccountName
        };
        await SaveAsync();
    }

    public async Task DisconnectAsync(SocialPlatform platform)
    {
        var account = RequireConfigured(platform); account.AccessToken = null; account.RefreshToken = null; account.ExpiresAt = null; account.AccountId = null; account.AccountName = null; await SaveAsync();
    }

    public string AuthorizationUrl(SocialPlatform platform, string baseUrl)
    {
        var account = RequireConfigured(platform);
        foreach (var expired in _states.Where(item => item.Value.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10)).Select(item => item.Key).ToList()) _states.Remove(expired);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        string? verifier = null; string? challenge = null;
        if (platform == SocialPlatform.TikTok)
        {
            verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)).ToLowerInvariant();
            challenge = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).ToLowerInvariant();
        }
        _states[state] = new PendingAuthorization(platform, verifier, DateTimeOffset.UtcNow);
        var redirect = Uri.EscapeDataString(Callback(platform, baseUrl));
        return platform switch
        {
            SocialPlatform.YouTube => $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.upload")}&access_type=offline&prompt=consent&state={state}",
            SocialPlatform.Instagram => $"https://www.instagram.com/oauth/authorize?enable_fb_login=0&force_authentication=1&client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=instagram_business_basic,instagram_business_content_publish&state={state}",
            SocialPlatform.TikTok => $"https://www.tiktok.com/v2/auth/authorize/?client_key={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=user.info.basic,video.publish&state={state}&code_challenge={challenge}&code_challenge_method=S256",
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
    }

    public async Task CompleteOAuthAsync(SocialPlatform platform, string code, string state, string baseUrl)
    {
        if (!_states.Remove(state, out var pending) || pending.Platform != platform || pending.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
            throw new InvalidOperationException("A solicitação de conexão expirou. Tente conectar novamente.");
        var account = RequireConfigured(platform);
        var fields = platform switch
        {
            SocialPlatform.YouTube => new Dictionary<string, string> { ["client_id"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl) },
            SocialPlatform.Instagram => new Dictionary<string, string> { ["client_id"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl) },
            _ => new Dictionary<string, string> { ["client_key"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl), ["code_verifier"] = pending.CodeVerifier! }
        };
        var endpoint = platform switch
        {
            SocialPlatform.YouTube => "https://oauth2.googleapis.com/token",
            SocialPlatform.Instagram => "https://api.instagram.com/oauth/access_token",
            _ => "https://open.tiktokapis.com/v2/oauth/token/"
        };
        using var response = await _http.CreateClient().PostAsync(endpoint, new FormUrlEncodedContent(fields));
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"A plataforma recusou a conexão: {raw}");
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        account.AccessToken = root.GetProperty("access_token").GetString();
        if (root.TryGetProperty("refresh_token", out var refresh)) account.RefreshToken = refresh.GetString();
        if (root.TryGetProperty("expires_in", out var expires)) account.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expires.GetDouble());
        if (root.TryGetProperty("user_id", out var userId)) account.AccountId = userId.ToString();
        if (root.TryGetProperty("open_id", out var openId)) account.AccountId = openId.GetString();
        account.AccountName = await ResolveAccountAsync(platform, account);
        await SaveAsync();
    }

    public async Task<PublicationRecord> PublishAsync(string projectId, string clipId, PublishRequest request)
    {
        var project = _projects.Get(projectId) ?? throw new InvalidOperationException("Projeto não encontrado.");
        var clip = project.Clips.FirstOrDefault(c => c.Id == clipId) ?? throw new InvalidOperationException("Corte não encontrado.");
        var path = string.IsNullOrWhiteSpace(clip.VideoPath) ? null : _projects.ResolveAsset(projectId, clip.VideoPath);
        if (path is null) throw new InvalidOperationException("Renderize o corte antes de publicar.");
        var quality = await _quality.ValidateAsync(project, clip);
        if (quality.Status == QualityStatus.Blocked) throw new InvalidOperationException($"Publicacao bloqueada pelo controle de qualidade (score {quality.Score}). Corrija ou reprocesse o corte.");
        SocialPublishingPolicy.Validate(request.Platform, path, clip.End - clip.Start, request);
        if (_history.Any(x => x.ProjectId == projectId && x.ClipId == clipId && x.Platform == request.Platform && x.Status is "scheduled" or "uploading" or "published"))
            throw new InvalidOperationException("Este corte já está agendado ou publicado nesta plataforma.");
        var scheduled = request.PublishAt is { } date && date > DateTimeOffset.UtcNow.AddSeconds(10);
        var record = new PublicationRecord { Platform = request.Platform, ProjectId = projectId, ClipId = clipId, Status = scheduled ? "scheduled" : "queued", ScheduledAt = request.PublishAt, Title = request.Title, Description = request.Description, Privacy = request.Privacy };
        _history.Add(record);
        await SaveAsync();
        if (scheduled) return record;
        await ExecuteAsync(record);
        return record;
    }

    public IReadOnlyList<PublicationRecord> Due() => _history.Where(x => x.Status == "scheduled" && x.ScheduledAt <= DateTimeOffset.UtcNow).OrderBy(x => x.ScheduledAt).ToList();

    public async Task<PublicationRecord> RetryAsync(string id)
    {
        var record = _history.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Status != "failed") throw new InvalidOperationException("Somente publicações com falha podem ser reenviadas.");
        record.Status = "scheduled"; record.ScheduledAt = DateTimeOffset.UtcNow; record.Error = null; record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(); return record;
    }

    public async Task<PublicationRecord> RescheduleAsync(string id, DateTimeOffset date)
    {
        var record = _history.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Publicacao nao encontrada.");
        if (record.Status is "published" or "uploading" or "cancelled") throw new InvalidOperationException("Esta publicacao nao pode ser reagendada.");
        if (date <= DateTimeOffset.UtcNow.AddMinutes(1)) throw new InvalidOperationException("Escolha um horario futuro ou use Publicar agora.");
        record.Status = "scheduled"; record.ScheduledAt = date; record.Error = null; record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(); return record;
    }

    public async Task<PublicationRecord> CancelPublicationAsync(string id)
    {
        var record = _history.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Publicacao nao encontrada.");
        if (record.Status is "published" or "uploading") throw new InvalidOperationException("Uma publicacao enviada nao pode ser cancelada localmente.");
        record.Status = "cancelled"; record.Error = null; record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(); return record;
    }

    public async Task<PublicationRecord> PublishNowAsync(string id)
    {
        var record = _history.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Publicacao nao encontrada.");
        if (record.Status is "published" or "uploading" or "cancelled") throw new InvalidOperationException("Esta publicacao nao pode ser enviada agora.");
        record.Status = "queued"; record.ScheduledAt = DateTimeOffset.UtcNow; record.Error = null; await SaveAsync(); await ExecuteAsync(record); return record;
    }

    public async Task ExecuteAsync(PublicationRecord record)
    {
        using var uploadSlot = await _workLimiter.EnterAsync(ProductionWorkKind.Upload);
        try
        {
            if (record.Status is "uploading" or "published") return;
            var project = _projects.Get(record.ProjectId) ?? throw new InvalidOperationException("Projeto não encontrado.");
            var clip = project.Clips.FirstOrDefault(c => c.Id == record.ClipId) ?? throw new InvalidOperationException("Corte não encontrado.");
            var path = string.IsNullOrWhiteSpace(clip.VideoPath) ? null : _projects.ResolveAsset(record.ProjectId, clip.VideoPath);
            if (path is null) throw new InvalidOperationException("Renderize o corte antes de publicar.");
            var quality = await _quality.ValidateAsync(project, clip);
            if (quality.Status == QualityStatus.Blocked) { record.Status = "failed"; record.Error = $"Publicacao bloqueada pelo Quality Gate (score {quality.Score})."; return; }
            record.Status = "uploading"; record.Attempts++; record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync();
            var request = new PublishRequest(record.Platform, record.Title, record.Description, record.Privacy);
            await EnsureFreshTokenAsync(record.Platform);
            if (record.Platform == SocialPlatform.YouTube) await PublishYouTubeAsync(path, request, record);
            else if (record.Platform == SocialPlatform.Instagram) await PublishInstagramAsync(record.ProjectId, clip, request, record);
            else await PublishTikTokAsync(path, request, record);
            record.Status = "published"; record.PublishedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            record.Error = ex.Message;
            if (record.Attempts < 3) { record.Status = "scheduled"; record.ScheduledAt = DateTimeOffset.UtcNow.Add(SocialPublishingPolicy.RetryDelay(record.Attempts)); }
            else record.Status = "failed";
        }
        finally { record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(); }
    }

    public IReadOnlyList<PublicationRecord> History() => _history.OrderByDescending(x => x.CreatedAt).ToList();

    private async Task PublishYouTubeAsync(string path, PublishRequest request, PublicationRecord record)
    {
        var account = RequireConnected(SocialPlatform.YouTube);
        using var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var title = request.Title[..Math.Min(100, request.Title.Length)];
        record.TotalBytes = new FileInfo(path).Length;
        if (string.IsNullOrWhiteSpace(record.UploadSessionUrl))
        {
            var metadata = JsonSerializer.Serialize(new { snippet = new { title, description = request.Description, categoryId = "22" }, status = new { privacyStatus = request.Privacy, selfDeclaredMadeForKids = false } });
            using var initialize = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
            initialize.Content = new StringContent(metadata, Encoding.UTF8, "application/json"); initialize.Headers.Add("X-Upload-Content-Type", "video/mp4"); initialize.Headers.Add("X-Upload-Content-Length", record.TotalBytes.ToString());
            using var initialized = await client.SendAsync(initialize); var responseText = await initialized.Content.ReadAsStringAsync(); if (!initialized.IsSuccessStatusCode) throw new InvalidOperationException(responseText);
            record.UploadSessionUrl = (initialized.Headers.Location ?? throw new InvalidOperationException("YouTube não retornou a sessão retomável.")).ToString(); record.UploadedBytes = 0; await SaveAsync();
        }
        const int chunkSize = 8 * 1024 * 1024; string raw = "";
        await using var stream = File.OpenRead(path); stream.Position = Math.Min(record.UploadedBytes, stream.Length);
        while (stream.Position < stream.Length)
        {
            var start = stream.Position; var length = (int)Math.Min(chunkSize, stream.Length - start); var buffer = new byte[length]; var read = await stream.ReadAsync(buffer); if (read == 0) break;
            using var message = new HttpRequestMessage(HttpMethod.Put, record.UploadSessionUrl); message.Content = new ByteArrayContent(buffer, 0, read); message.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4"); message.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + read - 1, stream.Length);
            using var uploaded = await client.SendAsync(message); raw = await uploaded.Content.ReadAsStringAsync();
            if ((int)uploaded.StatusCode == 308 || uploaded.IsSuccessStatusCode) { record.UploadedBytes = start + read; record.Progress = (int)Math.Round(record.UploadedBytes * 100d / record.TotalBytes); record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(); }
            if ((int)uploaded.StatusCode == 308) continue;
            if (!uploaded.IsSuccessStatusCode) throw new InvalidOperationException(raw);
            break;
        }
        using var doc = JsonDocument.Parse(raw);
        record.ExternalId = doc.RootElement.GetProperty("id").GetString();
        record.ExternalUrl = $"https://youtu.be/{record.ExternalId}"; record.Progress = 100; record.PlatformStatus = "processing"; record.UploadSessionUrl = null;
    }

    public async Task<PublicationRecord> RefreshStatusAsync(string id)
    {
        var record = _history.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Platform != SocialPlatform.YouTube || string.IsNullOrWhiteSpace(record.ExternalId)) return record;
        await EnsureFreshTokenAsync(SocialPlatform.YouTube);
        var account = RequireConnected(SocialPlatform.YouTube); using var client = _http.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var document = JsonDocument.Parse(await client.GetStringAsync($"https://www.googleapis.com/youtube/v3/videos?part=processingDetails,statistics&id={record.ExternalId}"));
        var items = document.RootElement.GetProperty("items"); if (items.GetArrayLength() > 0) record.PlatformStatus = items[0].TryGetProperty("processingDetails", out var details) && details.TryGetProperty("processingStatus", out var status) ? status.GetString() : "available";
        record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(); return record;
    }

    private async Task PublishInstagramAsync(string projectId, ClipCandidate clip, PublishRequest request, PublicationRecord record)
    {
        var account = RequireConnected(SocialPlatform.Instagram);
        if (string.IsNullOrWhiteSpace(account.PublicBaseUrl)) throw new InvalidOperationException("Instagram exige uma URL HTTPS pública para buscar o MP4.");
        if (string.IsNullOrWhiteSpace(account.AccountId)) throw new InvalidOperationException("A conta Instagram não forneceu um ID publicável.");
        var videoUrl = $"{account.PublicBaseUrl}/api/projects/{projectId}/assets/{Uri.EscapeDataString(clip.VideoPath!)}";
        using var client = _http.CreateClient();
        var create = await client.PostAsync($"https://graph.instagram.com/v24.0/{account.AccountId}/media", new FormUrlEncodedContent(new Dictionary<string, string> { ["media_type"] = "REELS", ["video_url"] = videoUrl, ["caption"] = request.Description, ["share_to_feed"] = "true", ["access_token"] = account.AccessToken! }));
        var raw = await create.Content.ReadAsStringAsync();
        if (!create.IsSuccessStatusCode) throw new InvalidOperationException(raw);
        using var createDoc = JsonDocument.Parse(raw);
        var container = createDoc.RootElement.GetProperty("id").GetString()!;
        var ready = false;
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(3000);
            var status = await client.GetStringAsync($"https://graph.instagram.com/v24.0/{container}?fields=status_code&access_token={Uri.EscapeDataString(account.AccessToken!)}");
            if (status.Contains("FINISHED")) { ready = true; break; }
            if (status.Contains("ERROR")) throw new InvalidOperationException(status);
        }
        if (!ready) throw new InvalidOperationException("Instagram não terminou de processar o vídeo no tempo esperado.");
        var publish = await client.PostAsync($"https://graph.instagram.com/v24.0/{account.AccountId}/media_publish", new FormUrlEncodedContent(new Dictionary<string, string> { ["creation_id"] = container, ["access_token"] = account.AccessToken! }));
        raw = await publish.Content.ReadAsStringAsync();
        if (!publish.IsSuccessStatusCode) throw new InvalidOperationException(raw);
        using var publishDoc = JsonDocument.Parse(raw);
        record.ExternalId = publishDoc.RootElement.GetProperty("id").GetString();
    }

    private async Task PublishTikTokAsync(string path, PublishRequest request, PublicationRecord record)
    {
        var account = RequireConnected(SocialPlatform.TikTok);
        var creator = await QueryTikTokCreatorAsync(account);
        var privacy = TikTokPublishingPolicy.PrivacyLevel(request.Privacy);
        if (!creator.PrivacyLevels.Contains(privacy, StringComparer.Ordinal))
            throw new InvalidOperationException("A privacidade escolhida não está disponível para esta conta TikTok. Reconecte a conta e tente novamente.");
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var size = new FileInfo(path).Length;
        var caption = request.Description[..Math.Min(2200, request.Description.Length)];
        var payload = JsonSerializer.Serialize(new { post_info = new { title = caption, privacy_level = privacy, disable_duet = creator.DuetDisabled, disable_comment = creator.CommentDisabled, disable_stitch = creator.StitchDisabled, video_cover_timestamp_ms = 1000 }, source_info = new { source = "FILE_UPLOAD", video_size = size, chunk_size = size, total_chunk_count = 1 } });
        using var initialized = await client.PostAsync("https://open.tiktokapis.com/v2/post/publish/video/init/", new StringContent(payload, Encoding.UTF8, "application/json"));
        var raw = await initialized.Content.ReadAsStringAsync();
        EnsureTikTokSuccess(initialized, raw, "iniciar o envio do vídeo");
        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data");
        var uploadUrl = data.GetProperty("upload_url").GetString()!;
        record.ExternalId = data.GetProperty("publish_id").GetString();
        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Headers.ContentRange = new ContentRangeHeaderValue(0, size - 1, size);
        using var uploaded = await client.PutAsync(uploadUrl, content);
        if (!uploaded.IsSuccessStatusCode) throw new InvalidOperationException(await uploaded.Content.ReadAsStringAsync());
        record.Progress = 100; record.PlatformStatus = "PROCESSING_UPLOAD"; await SaveAsync();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            using var statusRequest = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/status/fetch/") { Content = JsonContent.Create(new { publish_id = record.ExternalId }) };
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            using var statusResponse = await client.SendAsync(statusRequest); var statusRaw = await statusResponse.Content.ReadAsStringAsync();
            EnsureTikTokSuccess(statusResponse, statusRaw, "consultar o processamento do vídeo");
            using var statusDocument = JsonDocument.Parse(statusRaw); var statusData = statusDocument.RootElement.GetProperty("data");
            record.PlatformStatus = statusData.TryGetProperty("status", out var status) ? status.GetString() : record.PlatformStatus; record.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync();
            if (record.PlatformStatus == "PUBLISH_COMPLETE") return;
            if (record.PlatformStatus == "FAILED")
            {
                var reason = statusData.TryGetProperty("fail_reason", out var failure) ? failure.GetString() : "erro desconhecido";
                throw new InvalidOperationException($"O TikTok não conseguiu publicar o vídeo: {reason}");
            }
        }
        throw new InvalidOperationException("O TikTok ainda não concluiu o processamento. Tente atualizar o status em alguns minutos.");
    }

    private async Task<TikTokCreatorInfo> QueryTikTokCreatorAsync(SocialCredential account)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/creator_info/query/") { Content = JsonContent.Create(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.CreateClient().SendAsync(request); var raw = await response.Content.ReadAsStringAsync();
        EnsureTikTokSuccess(response, raw, "consultar as opções de publicação");
        using var document = JsonDocument.Parse(raw); var data = document.RootElement.GetProperty("data");
        var privacy = data.TryGetProperty("privacy_level_options", out var options) ? options.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray() : ["SELF_ONLY"];
        return new TikTokCreatorInfo(privacy, data.TryGetProperty("comment_disabled", out var comment) && comment.GetBoolean(), data.TryGetProperty("duet_disabled", out var duet) && duet.GetBoolean(), data.TryGetProperty("stitch_disabled", out var stitch) && stitch.GetBoolean());
    }

    private static void EnsureTikTokSuccess(HttpResponseMessage response, string raw, string action)
    {
        if (!response.IsSuccessStatusCode) throw TikTokError(action, raw);
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) && code.GetString() is { } value && value != "ok") throw TikTokError(action, raw);
        }
        catch (JsonException) { throw new InvalidOperationException($"O TikTok retornou uma resposta inválida ao {action}."); }
    }

    private static Exception TikTokError(string action, string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw); var root = document.RootElement; string? message = root.TryGetProperty("error_description", out var oauth) ? oauth.GetString() : null; string? code = null; string? logId = null;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) { message ??= error.TryGetProperty("message", out var api) ? api.GetString() : null; code = error.TryGetProperty("code", out var errorCode) ? errorCode.GetString() : null; logId = error.TryGetProperty("log_id", out var log) ? log.GetString() : null; }
            var details = string.Join("; ", new[] { code, logId is null ? null : $"log {logId}" }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new InvalidOperationException($"O TikTok recusou {action}{(details.Length > 0 ? $" ({details})" : "")}: {message ?? "erro desconhecido"}");
        }
        catch (JsonException) { return new InvalidOperationException($"O TikTok recusou {action}."); }
    }

    private async Task<string?> ResolveAccountAsync(SocialPlatform platform, SocialCredential account)
    {
        try
        {
            using var client = _http.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            var url = platform switch
            {
                SocialPlatform.YouTube => "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true",
                SocialPlatform.Instagram => $"https://graph.instagram.com/me?fields=id,user_id,username&access_token={Uri.EscapeDataString(account.AccessToken!)}",
                _ => "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name"
            };
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
            if (platform == SocialPlatform.YouTube) return doc.RootElement.GetProperty("items")[0].GetProperty("snippet").GetProperty("title").GetString();
            if (platform == SocialPlatform.Instagram)
            {
                account.AccountId = doc.RootElement.TryGetProperty("user_id", out var id) ? id.ToString() : doc.RootElement.GetProperty("id").ToString();
                return doc.RootElement.GetProperty("username").GetString();
            }
            return doc.RootElement.GetProperty("data").GetProperty("user").GetProperty("display_name").GetString();
        }
        catch { return platform.ToString(); }
    }

    private async Task EnsureFreshTokenAsync(SocialPlatform platform)
    {
        var account = RequireConnected(platform);
        if (account.ExpiresAt is null || account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) return;
        using var client = _http.CreateClient(); HttpResponseMessage response;
        if (platform == SocialPlatform.Instagram)
            response = await client.GetAsync($"https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(account.AccessToken!)}");
        else
        {
            if (string.IsNullOrWhiteSpace(account.RefreshToken)) throw new InvalidOperationException($"A conexão com {platform} expirou. Conecte a conta novamente.");
            var endpoint = platform == SocialPlatform.YouTube ? "https://oauth2.googleapis.com/token" : "https://open.tiktokapis.com/v2/oauth/token/";
            var fields = platform == SocialPlatform.YouTube
                ? new Dictionary<string, string> { ["client_id"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["refresh_token"] = account.RefreshToken, ["grant_type"] = "refresh_token" }
                : new Dictionary<string, string> { ["client_key"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["refresh_token"] = account.RefreshToken, ["grant_type"] = "refresh_token" };
            response = await client.PostAsync(endpoint, new FormUrlEncodedContent(fields));
        }
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Não foi possível renovar a conexão com {platform}: {raw}");
            using var document = JsonDocument.Parse(raw); var root = document.RootElement; account.AccessToken = root.GetProperty("access_token").GetString();
            if (root.TryGetProperty("refresh_token", out var refresh)) account.RefreshToken = refresh.GetString();
            account.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expires) ? expires.GetDouble() : 3600); await SaveAsync();
        }
    }

    private SocialCredential RequireConfigured(SocialPlatform platform) =>
        _accounts.TryGetValue(platform, out var account) && !string.IsNullOrWhiteSpace(account.ClientId)
            ? account : throw new InvalidOperationException($"Configure o aplicativo {platform} primeiro.");
    private SocialCredential RequireConnected(SocialPlatform platform)
    {
        var account = RequireConfigured(platform);
        return !string.IsNullOrWhiteSpace(account.AccessToken) ? account : throw new InvalidOperationException($"Conecte a conta {platform} primeiro.");
    }
    private static string Callback(SocialPlatform platform, string baseUrl) => $"{baseUrl.TrimEnd('/')}/api/social/callback/{platform.ToString().ToLowerInvariant()}";
    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            foreach (var item in JsonSerializer.Deserialize<List<SocialCredential>>(_protector.Unprotect(File.ReadAllText(_file)), JsonOptions) ?? [])
                _accounts[item.Platform] = item;
            var historyFile = _file + ".history";
            if (File.Exists(historyFile)) _history.AddRange(JsonSerializer.Deserialize<List<PublicationRecord>>(_protector.Unprotect(File.ReadAllText(historyFile)), JsonOptions) ?? []);
        }
        catch { }
    }
    private async Task SaveAsync()
    {
        await File.WriteAllTextAsync(_file, _protector.Protect(JsonSerializer.Serialize(_accounts.Values, JsonOptions)));
        await File.WriteAllTextAsync(_file + ".history", _protector.Protect(JsonSerializer.Serialize(_history, JsonOptions)));
    }
    private sealed record PendingAuthorization(SocialPlatform Platform, string? CodeVerifier, DateTimeOffset CreatedAt);
    private sealed record TikTokCreatorInfo(string[] PrivacyLevels, bool CommentDisabled, bool DuetDisabled, bool StitchDisabled);
}
