using System.Net.Http.Headers;
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
    private readonly Dictionary<SocialPlatform, SocialCredential> _accounts = [];
    private readonly Dictionary<string, SocialPlatform> _states = [];
    private readonly List<PublicationRecord> _history = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SocialService(IWebHostEnvironment env, IDataProtectionProvider dataProtection, IHttpClientFactory http, ProjectStore projects)
    {
        var root = Path.Combine(env.ContentRootPath, "storage", "social");
        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "credentials.protected");
        _protector = dataProtection.CreateProtector("CortaFeStudio.Social.v1");
        _http = http;
        _projects = projects;
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

    public string AuthorizationUrl(SocialPlatform platform, string baseUrl)
    {
        var account = RequireConfigured(platform);
        var state = Guid.NewGuid().ToString("N");
        _states[state] = platform;
        var redirect = Uri.EscapeDataString(Callback(platform, baseUrl));
        return platform switch
        {
            SocialPlatform.YouTube => $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.upload")}&access_type=offline&prompt=consent&state={state}",
            SocialPlatform.Instagram => $"https://www.instagram.com/oauth/authorize?enable_fb_login=0&force_authentication=1&client_id={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=instagram_business_basic,instagram_business_content_publish&state={state}",
            SocialPlatform.TikTok => $"https://www.tiktok.com/v2/auth/authorize/?client_key={Uri.EscapeDataString(account.ClientId)}&redirect_uri={redirect}&response_type=code&scope=user.info.basic,video.publish&state={state}",
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
    }

    public async Task CompleteOAuthAsync(SocialPlatform platform, string code, string state, string baseUrl)
    {
        if (!_states.Remove(state, out var expected) || expected != platform)
            throw new InvalidOperationException("A solicitação de conexão expirou. Tente conectar novamente.");
        var account = RequireConfigured(platform);
        var fields = platform switch
        {
            SocialPlatform.YouTube => new Dictionary<string, string> { ["client_id"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl) },
            SocialPlatform.Instagram => new Dictionary<string, string> { ["client_id"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl) },
            _ => new Dictionary<string, string> { ["client_key"] = account.ClientId, ["client_secret"] = account.ClientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = Callback(platform, baseUrl) }
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
        var record = new PublicationRecord { Platform = request.Platform, ProjectId = projectId, ClipId = clipId, Status = "uploading" };
        _history.Add(record);
        try
        {
            if (request.Platform == SocialPlatform.YouTube) await PublishYouTubeAsync(path, request, record);
            else if (request.Platform == SocialPlatform.Instagram) await PublishInstagramAsync(projectId, clip, request, record);
            else await PublishTikTokAsync(path, request, record);
            record.Status = "published";
        }
        catch (Exception ex) { record.Status = "failed"; record.Error = ex.Message; }
        return record;
    }

    public IReadOnlyList<PublicationRecord> History() => _history.OrderByDescending(x => x.CreatedAt).ToList();

    private async Task PublishYouTubeAsync(string path, PublishRequest request, PublicationRecord record)
    {
        var account = RequireConnected(SocialPlatform.YouTube);
        using var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var title = request.Title[..Math.Min(100, request.Title.Length)];
        var metadata = JsonSerializer.Serialize(new { snippet = new { title, description = request.Description, categoryId = "22" }, status = new { privacyStatus = request.Privacy, selfDeclaredMadeForKids = false, publishAt = request.PublishAt } });
        using var initialize = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
        initialize.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
        initialize.Headers.Add("X-Upload-Content-Type", "video/mp4");
        initialize.Headers.Add("X-Upload-Content-Length", new FileInfo(path).Length.ToString());
        using var initialized = await client.SendAsync(initialize);
        var raw = await initialized.Content.ReadAsStringAsync();
        if (!initialized.IsSuccessStatusCode) throw new InvalidOperationException(raw);
        var location = initialized.Headers.Location ?? throw new InvalidOperationException("YouTube não retornou a URL de upload.");
        await using var stream = File.OpenRead(path);
        using var video = new StreamContent(stream);
        video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var uploaded = await client.PutAsync(location, video);
        raw = await uploaded.Content.ReadAsStringAsync();
        if (!uploaded.IsSuccessStatusCode) throw new InvalidOperationException(raw);
        using var doc = JsonDocument.Parse(raw);
        record.ExternalId = doc.RootElement.GetProperty("id").GetString();
        record.ExternalUrl = $"https://youtu.be/{record.ExternalId}";
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
        using var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var size = new FileInfo(path).Length;
        var caption = request.Description[..Math.Min(2200, request.Description.Length)];
        var payload = JsonSerializer.Serialize(new { post_info = new { title = caption, privacy_level = request.Privacy == "public" ? "PUBLIC_TO_EVERYONE" : "SELF_ONLY", disable_duet = false, disable_comment = false, disable_stitch = false, video_cover_timestamp_ms = 1000 }, source_info = new { source = "FILE_UPLOAD", video_size = size, chunk_size = size, total_chunk_count = 1 } });
        using var initialized = await client.PostAsync("https://open.tiktokapis.com/v2/post/publish/video/init/", new StringContent(payload, Encoding.UTF8, "application/json"));
        var raw = await initialized.Content.ReadAsStringAsync();
        if (!initialized.IsSuccessStatusCode) throw new InvalidOperationException(raw);
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
        }
        catch { }
    }
    private Task SaveAsync() => File.WriteAllTextAsync(_file, _protector.Protect(JsonSerializer.Serialize(_accounts.Values, JsonOptions)));
}
