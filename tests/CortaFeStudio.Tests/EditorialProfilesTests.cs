using CortaFeStudio.Api.Services;
namespace CortaFeStudio.Tests;
public sealed class EditorialProfilesTests
{
    [Theory]
    [InlineData("podcast",35,90)] [InlineData("aula",30,75)] [InlineData("motivacao",20,50)] [InlineData("negocios",25,60)] [InlineData("tecnologia",30,90)]
    public void Get_RetornaPerfilEspecializado(string id,int min,int max) { var profile=EditorialProfiles.Get(id); Assert.Equal(min,profile.MinDuration); Assert.Equal(max,profile.MaxDuration); Assert.NotEmpty(profile.Signals); Assert.NotEmpty(profile.Hashtags); }
    [Fact] public void All_MantemPerfisOriginaisENovos()=>Assert.Equal(7,EditorialProfiles.All.Count);
}
