using CortaFeStudio.Api.Services;
namespace CortaFeStudio.Tests;
public sealed class EditorialProfilesTests
{
    [Theory]
    [InlineData("podcast",60,75)] [InlineData("aula",60,75)] [InlineData("motivacao",60,75)] [InlineData("negocios",60,75)] [InlineData("tecnologia",60,75)]
    public void Get_RetornaPerfilEspecializado(string id,int min,int max) { var profile=EditorialProfiles.Get(id); Assert.Equal(min,profile.MinDuration); Assert.Equal(max,profile.MaxDuration); Assert.NotEmpty(profile.Signals); Assert.NotEmpty(profile.Hashtags); }
    [Fact] public void All_MantemPerfisOriginaisENovos()=>Assert.Equal(7,EditorialProfiles.All.Count);
}
