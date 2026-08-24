using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ContentCalendarServiceTests
{
    [Fact]
    public void Plan_DistribuiPlataformasEClipesSemRepeticaoConsecutiva()
    {
        var clips = Clips();
        var plan = ContentCalendarService.Plan(Project(clips), clips, [SocialPlatform.YouTube, SocialPlatform.Instagram], Strategy());

        Assert.Equal(clips.Count * 2, plan.Count);
        Assert.Equal(clips.Count, plan.Count(item => item.Platform == SocialPlatform.YouTube));
        Assert.Equal(clips.Count, plan.Count(item => item.Platform == SocialPlatform.Instagram));
        for (var index = 1; index < plan.Count; index++) Assert.NotEqual(plan[index - 1].ClipId, plan[index].ClipId);
    }

    [Fact]
    public void Plan_RespeitaPostsPorDiaHorariosEIntervaloMinimo()
    {
        var clips = Clips(); var plan = ContentCalendarService.Plan(Project(clips), clips, [SocialPlatform.YouTube], Strategy());

        Assert.Equal(new DateOnly(2026, 8, 25), DateOnly.FromDateTime(plan[0].ScheduledAt.DateTime));
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(plan[0].ScheduledAt.DateTime));
        Assert.Equal(new TimeOnly(19, 0), TimeOnly.FromDateTime(plan[1].ScheduledAt.DateTime));
        Assert.Equal(new DateOnly(2026, 8, 26), DateOnly.FromDateTime(plan[2].ScheduledAt.DateTime));
    }

    [Fact]
    public void Plan_NaoConcentraTodosOsMelhoresNoPrimeiroDia()
    {
        var clips = Clips(); var plan = ContentCalendarService.Plan(Project(clips), clips, [SocialPlatform.YouTube], Strategy());
        var topTwo = clips.OrderByDescending(clip => clip.SocialScore.Potential).Take(2).Select(clip => clip.Id).ToHashSet();
        var dates = plan.Where(item => topTwo.Contains(item.ClipId)).Select(item => DateOnly.FromDateTime(item.ScheduledAt.DateTime)).Distinct();
        Assert.Equal(2, dates.Count());
    }

    private static SchedulingStrategy Strategy() => new() { PostsPerDay = 2, PreferredTimes = ["10:00", "10:30", "19:00"], StartDate = new DateOnly(2026, 8, 25), MinimumIntervalMinutes = 120 };
    private static VideoProject Project(List<ClipCandidate> clips) => new() { Id = "project", Name = "Mensagem", Clips = clips };
    private static List<ClipCandidate> Clips() => [Clip("a", 96, "fe"), Clip("b", 90, "familia"), Clip("c", 82, "coragem"), Clip("d", 74, "proposito")];
    private static ClipCandidate Clip(string id, double score, string title) => new() { Id = id, Title = title, EditorialProfile = title, SocialScore = new SocialScoreBreakdown { Potential = score } };
}
