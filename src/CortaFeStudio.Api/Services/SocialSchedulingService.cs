using System.Text.Json;
using CortaFeStudio.Api.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CortaFeStudio.Api.Services;

public sealed class SocialSchedulingService
{
    private readonly SocialService _social;
    private readonly IDataProtector _protector;
    private readonly string _historyFile;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SocialSchedulingService(
        IWebHostEnvironment env,
        IDataProtectionProvider dataProtection,
        SocialService social)
    {
        _social = social;
        _protector = dataProtection.CreateProtector("CortaFeStudio.Social.v1");
        var root = Path.Combine(env.ContentRootPath, "storage", "social");
        Directory.CreateDirectory(root);
        _historyFile = Path.Combine(root, "credentials.protected.history");
    }

    public async Task<PublicationRecord> CancelAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var record = Find(id);
            if (record.Status != "scheduled")
                throw new InvalidOperationException("Somente publicações agendadas podem ser canceladas.");

            record.Status = "cancelled";
            record.ScheduledAt = null;
            record.Error = null;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveHistoryAsync();
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PublicationRecord> RescheduleAsync(
        string id,
        DateTimeOffset publishAt)
    {
        if (publishAt <= DateTimeOffset.UtcNow.AddSeconds(10))
            throw new InvalidOperationException("Escolha um horário futuro para reagendar.");

        await _lock.WaitAsync();
        try
        {
            var record = Find(id);
            if (record.Status is "uploading" or "published")
                throw new InvalidOperationException("Esta publicação já foi enviada e não pode ser reagendada.");

            record.Status = "scheduled";
            record.ScheduledAt = publishAt;
            record.PublishedAt = null;
            record.Error = null;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveHistoryAsync();
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PublicationRecord> PublishNowAsync(string id)
    {
        PublicationRecord record;

        await _lock.WaitAsync();
        try
        {
            record = Find(id);
            if (record.Status is "uploading" or "published")
                throw new InvalidOperationException("Esta publicação já foi enviada.");

            record.Status = "queued";
            record.ScheduledAt = null;
            record.Error = null;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveHistoryAsync();
        }
        finally
        {
            _lock.Release();
        }

        await _social.ExecuteAsync(record);
        return record;
    }

    private PublicationRecord Find(string id) =>
        _social.History().FirstOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException("Publicação não encontrada.");

    private async Task SaveHistoryAsync()
    {
        var json = JsonSerializer.Serialize(_social.History(), JsonOptions);
        await File.WriteAllTextAsync(_historyFile, _protector.Protect(json));
    }
}

public sealed record ReschedulePublicationRequest(DateTimeOffset PublishAt);
