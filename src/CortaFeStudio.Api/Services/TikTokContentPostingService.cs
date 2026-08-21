using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class TikTokContentPostingService(IHttpClientFactory http)
{
    public const long MinChunkSize = 5L * 1024 * 1024;
    public const long PreferredChunkSize = 10L * 1024 * 1024;
    public const long MaxChunkSize = 64L * 1024 * 1024;

    public async Task<TikTokCreatorInfo> CreatorInfoAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/creator_info/query/")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        EnsureTikTokSuccess(response, raw, "consultar as permissões da conta");
        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data");
        return new TikTokCreatorInfo
        {
            Username = data.TryGetProperty("creator_username", out var username) ? username.GetString() : null,
            Nickname = data.TryGetProperty("creator_nickname", out var nickname) ? nickname.GetString() : null,
            AvatarUrl = data.TryGetProperty("creator_avatar_url", out var avatar) ? avatar.GetString() : null,
            PrivacyLevelOptions = data.TryGetProperty("privacy_level_options", out var privacy) && privacy.ValueKind == JsonValueKind.Array
                ? privacy.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList()
                : [],
            CommentDisabled = data.TryGetProperty("comment_disabled", out var comments) && comments.GetBoolean(),
            DuetDisabled = data.TryGetProperty("duet_disabled", out var duet) && duet.GetBoolean(),
            StitchDisabled = data.TryGetProperty("stitch_disabled", out var stitch) && stitch.GetBoolean(),
            MaxVideoPostDurationSec = data.TryGetProperty("max_video_post_duration_sec", out var duration) ? duration.GetInt32() : 180
        };
    }

    public async Task PublishAsync(
        string path,
        double durationSeconds,
        string accessToken,
        PublishRequest request,
        PublicationRecord record,
        CancellationToken ct = default)
    {
        var creator = await CreatorInfoAsync(accessToken, ct);
        if (creator.MaxVideoPostDurationSec > 0 && durationSeconds > creator.MaxVideoPostDurationSec + .1)
            throw new InvalidOperationException($"Esta conta TikTok permite vídeos de até {creator.MaxVideoPostDurationSec} segundos.");

        var privacy = ResolvePrivacy(request.Privacy, creator.PrivacyLevelOptions);
        var size = new FileInfo(path).Length;
        var chunk = ChunkPlan(size);
        var caption = request.Description[..Math.Min(2200, request.Description.Length)];

        using var client = http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var payload = JsonSerializer.Serialize(new
        {
            post_info = new
            {
                title = caption,
                privacy_level = privacy,
                disable_duet = creator.DuetDisabled,
                disable_comment = creator.CommentDisabled,
                disable_stitch = creator.StitchDisabled,
                video_cover_timestamp_ms = 1000
            },
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = size,
                chunk_size = chunk.ChunkSize,
                total_chunk_count = chunk.TotalChunks
            }
        });

        using var initialized = await client.PostAsync(
            "https://open.tiktokapis.com/v2/post/publish/video/init/",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        var raw = await initialized.Content.ReadAsStringAsync(ct);
        EnsureTikTokSuccess(initialized, raw, "inicializar a publicação");
        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data");
        var uploadUrl = data.GetProperty("upload_url").GetString()
            ?? throw new InvalidOperationException("TikTok não retornou a URL de upload.");
        record.ExternalId = data.GetProperty("publish_id").GetString();
        record.PlatformStatus = "uploading";
        record.TotalBytes = size;
        record.UploadedBytes = 0;
        record.Progress = 0;

        await UploadChunksAsync(uploadUrl, path, chunk.ChunkSize, record, ct);
        record.PlatformStatus = "processing";
        record.Progress = 100;
    }

    public async Task<string?> QueryStatusAsync(string accessToken, string publishId, CancellationToken ct = default)
    {
        using var client = http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var payload = JsonSerializer.Serialize(new { publish_id = publishId });
        using var response = await client.PostAsync(
            "https://open.tiktokapis.com/v2/post/publish/status/fetch/",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        EnsureTikTokSuccess(response, raw, "consultar o status da publicação");
        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data");
        return data.TryGetProperty("status", out var status) ? status.GetString() : null;
    }

    public static string ResolvePrivacy(string requested, IReadOnlyCollection<string> allowed)
    {
        if (allowed.Count == 0)
            throw new InvalidOperationException("TikTok não retornou opções de privacidade para esta conta.");

        var normalized = requested.Trim().ToLowerInvariant() switch
        {
            "public" or "public_to_everyone" => "PUBLIC_TO_EVERYONE",
            "friends" or "mutual_follow_friends" => "MUTUAL_FOLLOW_FRIENDS",
            "followers" or "follower_of_creator" => "FOLLOWER_OF_CREATOR",
            _ => "SELF_ONLY"
        };

        if (allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return normalized;
        if (allowed.Contains("SELF_ONLY", StringComparer.OrdinalIgnoreCase))
            return "SELF_ONLY";

        throw new InvalidOperationException("A privacidade escolhida não está disponível para esta conta TikTok.");
    }

    public static (long ChunkSize, int TotalChunks) ChunkPlan(long videoSize)
    {
        if (videoSize <= 0) throw new ArgumentOutOfRangeException(nameof(videoSize));
        if (videoSize < MinChunkSize) return (videoSize, 1);

        var chunkSize = Math.Min(PreferredChunkSize, MaxChunkSize);
        var total = Math.Max(1, (int)(videoSize / chunkSize));
        if (total > 1000)
        {
            chunkSize = (long)Math.Ceiling(videoSize / 1000d);
            chunkSize = Math.Min(Math.Max(chunkSize, MinChunkSize), MaxChunkSize);
            total = Math.Max(1, (int)(videoSize / chunkSize));
        }
        return (chunkSize, total);
    }

    private async Task UploadChunksAsync(string uploadUrl, string path, long chunkSize, PublicationRecord record, CancellationToken ct)
    {
        var total = record.TotalBytes;
        await using var stream = File.OpenRead(path);
        var start = 0L;
        while (start < total)
        {
            var remaining = total - start;
            var length = remaining <= chunkSize * 2 ? remaining : chunkSize;
            var buffer = new byte[checked((int)length)];
            var read = 0;
            while (read < buffer.Length)
            {
                var current = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
                if (current == 0) break;
                read += current;
            }
            if (read == 0) break;

            using var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            message.Content = new ByteArrayContent(buffer, 0, read);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            message.Content.Headers.ContentLength = read;
            message.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + read - 1, total);
            using var response = await http.CreateClient().SendAsync(message, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"TikTok recusou um bloco do vídeo: {raw}");

            start += read;
            record.UploadedBytes = start;
            record.Progress = (int)Math.Round(start * 100d / total);
        }
    }

    private static void EnsureTikTokSuccess(HttpResponseMessage response, string raw, string action)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Não foi possível {action} no TikTok: {raw}");
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code) &&
                !string.Equals(code.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = error.TryGetProperty("message", out var msg) ? msg.GetString() : raw;
                throw new InvalidOperationException($"TikTok recusou a operação: {message}");
            }
        }
        catch (JsonException)
        {
        }
    }
}
