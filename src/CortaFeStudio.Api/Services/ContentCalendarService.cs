using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ContentCalendarService(ProjectStore projects, SocialService social)
{
    public IReadOnlyList<ScheduledContentItem> List() => social.History().Select(ToCalendarItem).OrderBy(item => item.ScheduledAt).ToList();

    public async Task<IReadOnlyList<ScheduledContentItem>> ScheduleAsync(VideoProject project, IReadOnlyList<ClipCandidate> clips, IReadOnlyList<SocialPlatform> platforms, SchedulingStrategy strategy)
    {
        var plan = Plan(project, clips, platforms, strategy); var result = new List<ScheduledContentItem>();
        foreach (var item in plan)
        {
            var clip = project.Clips.First(candidate => candidate.Id == item.ClipId);
            if (string.IsNullOrWhiteSpace(clip.PlatformMetadata.YouTube.Title)) ShortFormMetadataService.ApplyPlatformMetadata(clip, project.Options.ContentType);
            var metadata = ShortFormMetadataService.ForPlatform(clip, item.Platform);
            var record = await social.PublishAsync(project.Id, clip.Id, new PublishRequest(item.Platform, metadata.Title, metadata.Description, "private", item.ScheduledAt));
            item.PublicationId = record.Id; item.Status = record.Status; result.Add(item);
        }
        return result;
    }

    public static IReadOnlyList<ScheduledContentItem> Plan(VideoProject project, IReadOnlyList<ClipCandidate> source, IReadOnlyList<SocialPlatform> requestedPlatforms, SchedulingStrategy strategy)
    {
        var clips = Balance(source.OrderByDescending(clip => clip.SocialScore.Potential).ThenByDescending(clip => clip.Score).ToList());
        var platforms = requestedPlatforms.Distinct().DefaultIfEmpty(SocialPlatform.YouTube).ToList();
        var slots = BuildSlots(strategy, clips.Count * platforms.Count); var jobs = new List<(ClipCandidate Clip, SocialPlatform Platform)>();
        for (var platformIndex = 0; platformIndex < platforms.Count; platformIndex++)
            for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++) jobs.Add((clips[(clipIndex + platformIndex) % clips.Count], platforms[platformIndex]));
        return jobs.Select((job, index) => new ScheduledContentItem { ProjectId = project.Id, ProjectName = project.Name, ClipId = job.Clip.Id, ClipTitle = job.Clip.Title, EditorialProfile = job.Clip.EditorialProfile, SocialScore = job.Clip.SocialScore.Potential, Platform = job.Platform, ScheduledAt = slots[index] }).ToList();
    }

    public async Task<PublicationRecord> RescheduleAsync(string publicationId, DateTimeOffset date) => await social.RescheduleAsync(publicationId, date);
    public async Task<PublicationRecord> PublishNowAsync(string publicationId) => await social.PublishNowAsync(publicationId);
    public async Task<PublicationRecord> CancelAsync(string publicationId) => await social.CancelPublicationAsync(publicationId);
    public async Task<PublicationRecord> RetryAsync(string publicationId) => await social.RetryAsync(publicationId);

    private ScheduledContentItem ToCalendarItem(PublicationRecord record)
    {
        var project = projects.Get(record.ProjectId); var clip = project?.Clips.FirstOrDefault(item => item.Id == record.ClipId);
        return new ScheduledContentItem { PublicationId = record.Id, ProjectId = record.ProjectId, ProjectName = project?.Name ?? "Projeto removido", ClipId = record.ClipId, ClipTitle = clip?.Title ?? record.Title, EditorialProfile = clip?.EditorialProfile ?? "", SocialScore = clip?.SocialScore.Potential ?? 0, Platform = record.Platform, ScheduledAt = record.ScheduledAt ?? record.PublishedAt ?? record.CreatedAt, Status = record.Status, Error = record.Error };
    }

    private static IReadOnlyList<DateTimeOffset> BuildSlots(SchedulingStrategy input, int count)
    {
        var posts = Math.Clamp(input.PostsPerDay, 1, 12); var minimum = Math.Clamp(input.MinimumIntervalMinutes, 15, 720);
        var times = input.PreferredTimes.Select(value => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? (TimeOnly?)time : null).Where(value => value.HasValue).Select(value => value!.Value).Distinct().Order().ToList();
        if (times.Count == 0) times = [new TimeOnly(10, 0), new TimeOnly(19, 0)];
        var safeTimes = new List<TimeOnly>(); foreach (var time in times) if (safeTimes.Count == 0 || (time - safeTimes[^1]).TotalMinutes >= minimum) safeTimes.Add(time);
        posts = Math.Min(posts, safeTimes.Count); var result = new List<DateTimeOffset>(); var date = input.StartDate;
        while (result.Count < count) { foreach (var time in safeTimes.Take(posts)) { var local = date.ToDateTime(time); result.Add(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))); if (result.Count == count) break; } date = date.AddDays(1); }
        return result;
    }

    private static List<ClipCandidate> Balance(List<ClipCandidate> sorted)
    {
        var result = new List<ClipCandidate>(); var left = 0; var right = sorted.Count - 1;
        while (left <= right) { result.Add(sorted[left++]); if (left <= right) result.Add(sorted[right--]); }
        return result;
    }
}
