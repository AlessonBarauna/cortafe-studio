using System.Net.Http.Headers;
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
    private readonly TikTokContentPostingService _tiktok;
    private readonly Dictionary<SocialPlatform, SocialCredential> _accounts = [];
    private readonly Dictionary<string, SocialPlatform> _states = [];
    private readonly Dictionary<string, string> _tiktokPkce = [];
    private readonly List<PublicationRecord> _history = [];
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SocialService(IWebHostEnvironment env, IDataProtectionProvider dataProtection, IHttpClientFactory http, ProjectStore projects)
    {
        var root = Path.Combine(env.ContentRootPath, "storage", "social");
        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "credentials.protected");
        _protector = dataProtection.CreateProtector("CortaFeStudio.Social.v1");
        _http = http;
        _projects = projects;
        _tiktok = new TikTokContentPostingService(http);
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

    public async Task<TikTokCreatorInfo> TikTokCreatorInfoAsync(CancellationToken ct = default)
    {
        await EnsureFreshTokenAsync(SocialPlatform.TikTok);
        var account = RequireConnected(SocialPlatform.TikTok);
        return await _tiktok.CreatorInfoAsync(account.AccessToken!, ct);
    }

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
        var account = RequireConfigured(platform);
        account.AccessToken = null;
        account.RefreshToken = null;
        account.ExpiresAt = null;
        account.AccountId = null;
        account.AccountName = null;
        await SaveAsync();
    }

    public string AuthorizationUrl(SocialPlatform platform, string baseUrl)
    {
        var account = RequireConfigured(platform);
        var state = Guid.NewGuid().ToString("N");
        _states[state] = platform;
        var callback = Callback(platform, baseUrl);
        var redirect = Uri.EscapeDataString(callback);

        if (platform == SocialPlatform.TikTok)
        {
            var verifier = GenerateCodeVerifier();
            _tiktokPkce[state] = verifier;
            var challenge = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).ToLowerInvariant();
            return $"https://www.tiktok.com/v2/auth/authorize/?client_key={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=user.info.basic,video.publish&state={state}&code_challenge={challenge}&code_challenge_method=S256";
        }

        return platform switch
        {
            SocialPlatform.YouTube => $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.upload")}&access_type=offline&prompt=consent&state={state}",
            SocialPlatform.Instagram => $"https://www.instagram.com/oauth/authorize?enable_fb_login=0&force_authentication=1&client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=instagram_business_basic,instagram_business_content_publish&state={state}",
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
    }

    public async Task CompleteOAuthAsync(SocialPlatform platform, string code, string state, string baseUrl)
    {
        if (!_states.Remove(state, out var expected) || expected != platform)
            throw new InvalidOperationException("A solicitação de conexão expirou. Tente conectar novamente.");

        var account = RequireConfigured(platform);
        _tiktokPkce.Remove(state, out var verifier);
        var fields = platform switch
        {
            SocialPlatform.YouTube => new Dictionary<string, string>
            {
                ["client_id"] = account.ClientId,
                ["client_secret"] = account.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = Callback(platform, baseUrl)
            },
            SocialPlatform.Instagram => new Dictionary<string, string>
            {
                ["client_id"] = account.ClientId,
                ["client_secret"] = account.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = Callback(platform, baseUrl)
            },
            _ => new Dictionary<string, string>
            {
                ["client_key"] = account.ClientId,
                ["client_secret"] = account.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = Callback(platform, baseUrl),
                ["code_verifier"] = verifier ?? throw new InvalidOperationException("A autorização TikTok expirou. Conecte novamente.")
            }
        };

        var endpoint = platform switch
        {
            SocialPlatform.YouTube => "https://oauth2.googleapis.com/token",
            SocialPlatform.Instagram => "https://api.instagram.com/oauth/access_token",
            _ => "https://open.tiktokapis.com/v2/oauth/token/"
        };

        using var response = await _http.CreateClient().PostAsync(endpoint, new FormUrlEncodedContent(fields));
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"A plataforma recusou a conexão: {raw}");

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

        if (request.Platform == SocialPlatform.TikTok)
            RequireConnected(SocialPlatform.TikTok);

        SocialPublishingPolicy.Validate(request.Platform, path, clip.End - clip.Start, request);
        if (_history.Any(x => x.ProjectId == projectId && x.ClipId == clipId && x.Platform == request.Platform && x.Status is "scheduled" or "uploading" or "published"))
            throw new InvalidOperationException("Este corte já está agendado ou publicado nesta plataforma.");

        var scheduled = request.PublishAt is { } date && date > DateTimeOffset.UtcNow.AddSeconds(10);
        var record = new PublicationRecord
        {
            Platform = request.Platform,
            ProjectId = projectId,
            ClipId = clipId,
            Status = scheduled ? "scheduled" : "queued",
            ScheduledAt = request.PublishAt,
            Title = request.Title,
            Description = request.Description,
            Privacy = request.Privacy
        };
        _history.Add(record);
        await SaveAsync();
        if (scheduled) return record;
        await ExecuteAsync(record);
        return record;
    }

    public IReadOnlyList<PublicationRecord> Due() => _history
        .Where(x => x.Status == "scheduled" && x.ScheduledAt <= DateTimeOffset.UtcNow)
        .OrderBy(x => x.ScheduledAt)
        .ToList();

    public async Task<PublicationRecord> RetryAsync(string id)
    {
        var record = _history.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Status != "failed") throw new InvalidOperationException("Somente publicações com falha podem ser reenviadas.");
        RequireConnected(record.Platform);
        record.Status = "scheduled";
        record.ScheduledAt = DateTimeOffset.UtcNow;
        record.Error = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
        return record;
    }

    public async Task<PublicationRecord> ExecuteNowAsync(string id)
    {
        var record = _history.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Status is "uploading" or "published") throw new InvalidOperationException("Esta publicação já está em andamento ou concluída.");
        RequireConnected(record.Platform);
        record.Status = "queued";
        record.ScheduledAt = null;
        record.Error = null;
        await SaveAsync();
        await ExecuteAsync(record);
        return record;
    }

    public async Task<PublicationRecord> RescheduleAsync(string id, DateTimeOffset publishAt)
    {
        var record = _history.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Status is "uploading" or "published") throw new InvalidOperationException("Não é possível reagendar uma publicação já enviada.");
        if (publishAt <= DateTimeOffset.UtcNow.AddSeconds(10)) throw new InvalidOperationException("Escolha uma data futura para reagendar.");
        RequireConnected(record.Platform);
        record.Status = "scheduled";
        record.ScheduledAt = publishAt;
        record.Error = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
        return record;
    }

    public async Task<PublicationRecord> CancelAsync(string id)
    {
        var record = _history.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (record.Status == "uploading") throw new InvalidOperationException("O upload já começou e não pode ser cancelado por esta tela.");
        if (record.Status == "published") throw new InvalidOperationException("A publicação já foi enviada.");
        record.Status = "cancelled";
        record.ScheduledAt = null;
        record.Error = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
        return record;
    }

    public async Task ExecuteAsync(PublicationRecord record)
    {
        await _publishLock.WaitAsync();
        try
        {
            if (record.Status is "uploading" or "published" or "cancelled") return;
            var project = _projects.Get(record.ProjectId) ?? throw new InvalidOperationException("Projeto não encontrado.");
            var clip = project.Clips.FirstOrDefault(c => c.Id == record.ClipId) ?? throw new InvalidOperationException("Corte não encontrado.");
            var path = string.IsNullOrWhiteSpace(clip.VideoPath) ? null : _projects.ResolveAsset(record.ProjectId, clip.VideoPath);
            if (path is null) throw new InvalidOperationException("Renderize o corte antes de publicar.");

            record.Status = "uploading";
            record.Attempts++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync();

            var request = new PublishRequest(record.Platform, record.Title, record.Description, record.Privacy);
            await EnsureFreshTokenAsync(record.Platform);
            if (record.Platform == SocialPlatform.YouTube)
                await PublishYouTubeAsync(path, request, record);
            else if (record.Platform == SocialPlatform.Instagram)
                await PublishInstagramAsync(record.ProjectId, clip, request, record);
            else
                await PublishTikTokAsync(path, clip.End - clip.Start, request, record);

            record.Status = "published";
            record.PublishedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            record.Error = ex.Message;
            if (IsPermanentFailure(ex))
                record.Status = "failed";
            else if (record.Attempts < 3)
            {
                record.Status = "scheduled";
                record.ScheduledAt = DateTimeOffset.UtcNow.Add(SocialPublishingPolicy.RetryDelay(record.Attempts));
            }
            else
                record.Status = "failed";
        }
        finally
        {
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync();
            _publishLock.Release();
        }
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
            initialize.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
            initialize.Headers.Add("X-Upload-Content-Type", "video/mp4");
            initialize.Headers.Add("X-Upload-Content-Length", record.TotalBytes.ToString());
            using var initialized = await client.SendAsync(initialize);
            var responseText = await initialized.Content.ReadAsStringAsync();
            if (!initialized.IsSuccessStatusCode) throw new InvalidOperationException(responseText);
            record.UploadSessionUrl = (initialized.Headers.Location ?? throw new InvalidOperationException("YouTube não retornou a sessão retomável.")).ToString();
            record.UploadedBytes = 0;
            await SaveAsync();
        }
        const int chunkSize = 8 * 1024 * 1024;
        string raw = "";
        await using var stream = File.OpenRead(path);
        stream.Position = Math.Min(record.UploadedBytes, stream.Length);
        while (stream.Position < stream.Length)
        {
            var start = stream.Position;
            var length = (int)Math.Min(chunkSize, stream.Length - start);
            var buffer = new byte[length];
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            using var message = new HttpRequestMessage(HttpMethod.Put, record.UploadSessionUrl);
            message.Content = new ByteArrayContent(buffer, 0, read);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            message.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + read - 1, stream.Length);
            using var uploaded = await client.SendAsync(message);
            raw = await uploaded.Content.ReadAsStringAsync();
            if ((int)uploaded.StatusCode == 308 || uploaded.IsSuccessStatusCode)
            {
                record.UploadedBytes = start + read;
                record.Progress = (int)Math.Round(record.UploadedBytes * 100d / record.TotalBytes);
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveAsync();
            }
            if ((int)uploaded.StatusCode == 308) continue;
            if (!uploaded.IsSuccessStatusCode) throw new InvalidOperationException(raw);
            break;
        }
        using var doc = JsonDocument.Parse(raw);
        record.ExternalId = doc.RootElement.GetProperty("id").GetString();
        record.ExternalUrl = $"https://youtu.be/{record.ExternalId}";
        record.Progress = 100;
        record.PlatformStatus = "processing";
        record.UploadSessionUrl = null;
    }

    public async Task<PublicationRecord> RefreshStatusAsync(string id)
    {
        var record = _history.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Publicação não encontrada.");
        if (string.IsNullOrWhiteSpace(record.ExternalId)) return record;

        if (record.Platform == SocialPlatform.TikTok)
        {
            await EnsureFreshTokenAsync(SocialPlatform.TikTok);
            var account = RequireConnected(SocialPlatform.TikTok);
            record.PlatformStatus = await _tiktok.QueryStatusAsync(account.AccessToken!, record.ExternalId);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync();
            return record;
        }

        if (record.Platform != SocialPlatform.YouTube) return record;
        await EnsureFreshTokenAsync(SocialPlatform.YouTube);
        var youtube = RequireConnected(SocialPlatform.YouTube);
        using var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", youtube.AccessToken);
        using var document = JsonDocument.Parse(await client.GetStringAsync($"https://www.googleapis.com/youtube/v3/videos?part=processingDetails,statistics&id={record.ExternalId}"));
        var items = document.RootElement.GetProperty("items");
        if (items.GetArrayLength() > 0)
            record.PlatformStatus = items[0].TryGetProperty("processingDetails", out var details) && details.TryGetProperty("processingStatus", out var status) ? status.GetString() : "available";
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
        return record;
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

    private async Task PublishTikTokAsync(string path, double duration, PublishRequest request, PublicationRecord record)
    {
        var account = RequireConnected(SocialPlatform.TikTok);
        await _tiktok.PublishAsync(path, duration, account.AccessToken!, request, record);
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
        using var client = _http.CreateClient();
        HttpResponseMessage response;
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
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Não foi possível renovar a conexão com {platform}: {raw}");
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            account.AccessToken = root.GetProperty("access_token").GetString();
            if (root.TryGetProperty("refresh_token", out var refresh)) account.RefreshToken = refresh.GetString();
            account.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expires) ? expires.GetDouble() : 3600);
            await SaveAsync();
        }
    }

    private SocialCredential RequireConfigured(SocialPlatform platform) =>
        _accounts.TryGetValue(platform, out var account) && !string.IsNullOrWhiteSpace(account.ClientId)
            ? account
            : throw new InvalidOperationException($"Configure o aplicativo {platform} primeiro.");

    private SocialCredential RequireConnected(SocialPlatform platform)
    {
        var account = RequireConfigured(platform);
        return !string.IsNullOrWhiteSpace(account.AccessToken)
            ? account
            : throw new InvalidOperationException($"Conecte a conta {platform} primeiro.");
    }

    private static bool IsPermanentFailure(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("Configure o aplicativo", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Conecte a conta", StringComparison.OrdinalIgnoreCase)
            || message.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || message.Contains("privacidade escolhida", StringComparison.OrdinalIgnoreCase)
            || message.Contains("permite vídeos de até", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateCodeVerifier() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string Callback(SocialPlatform platform, string baseUrl) => $"{baseUrl.TrimEnd('/')}/api/social/callback/{platform.ToString().ToLowerInvariant()}";

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            foreach (var item in JsonSerializer.Deserialize<List<SocialCredential>>(_protector.Unprotect(File.ReadAllText(_file)), JsonOptions) ?? [])
                _accounts[item.Platform] = item;
            var historyFile = _file + ".history";
            if (File.Exists(historyFile))
                _history.AddRange(JsonSerializer.Deserialize<List<PublicationRecord>>(_protector.Unprotect(File.ReadAllText(historyFile)), JsonOptions) ?? []);
        }
        catch { }
    }

    private async Task SaveAsync()
    {
        await File.WriteAllTextAsync(_file, _protector.Protect(JsonSerializer.Serialize(_accounts.Values, JsonOptions)));
        await File.WriteAllTextAsync(_file + ".history", _protector.Protect(JsonSerializer.Serialize(_history, JsonOptions)));
    }
}
