using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ProjectRetentionServiceTests
{
    [Fact]
    public void Eligible_ProtegeFavoritosFixadosEProjetosAtivos()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7); var policy = new RetentionPolicy();
        Assert.True(ProjectRetentionService.Eligible(new VideoProject { Status = ProjectStatus.Ready, UpdatedAt = cutoff.AddDays(-1) }, policy, cutoff));
        Assert.False(ProjectRetentionService.Eligible(new VideoProject { Status = ProjectStatus.Ready, UpdatedAt = cutoff.AddDays(-1), Favorite = true }, policy, cutoff));
        Assert.False(ProjectRetentionService.Eligible(new VideoProject { Status = ProjectStatus.Ready, UpdatedAt = cutoff.AddDays(-1), Pinned = true }, policy, cutoff));
        Assert.False(ProjectRetentionService.Eligible(new VideoProject { Status = ProjectStatus.Analyzing, UpdatedAt = cutoff.AddDays(-1) }, policy, cutoff));
    }

    [Fact]
    public void Eligible_NaoRepeteLimpezaDeDadosJaExecutada()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var project = new VideoProject { Status = ProjectStatus.Ready, UpdatedAt = cutoff.AddDays(-1), DataPurgedAt = DateTime.UtcNow };
        Assert.False(ProjectRetentionService.Eligible(project, new RetentionPolicy { Mode = RetentionCleanupMode.ProjectData }, cutoff));
        Assert.True(ProjectRetentionService.Eligible(project, new RetentionPolicy { Mode = RetentionCleanupMode.FullProject }, cutoff));
    }
}
