using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class PlatformMetadataTests
{
    [Fact]
    public void ApplyPlatformMetadata_GeraCopyDiferentePorRede()
    {
        var clip = Clip();
        ShortFormMetadataService.ApplyPlatformMetadata(clip, "pregacao");

        Assert.NotEqual(clip.PlatformMetadata.YouTube.Description, clip.PlatformMetadata.Instagram.Caption);
        Assert.Contains("Compartilhe", clip.PlatformMetadata.YouTube.CallToAction);
        Assert.Contains("despertou", clip.PlatformMetadata.Instagram.CallToAction);
        Assert.False(string.IsNullOrWhiteSpace(clip.PlatformMetadata.TikTok.Caption));
    }

    [Fact]
    public void ApplyPlatformMetadata_RemoveHashtagsGenericasDeTodasAsRedes()
    {
        var clip = Clip(); clip.Hashtags = ["#fyp", "#viral", "#promessa", "#fe"];
        ShortFormMetadataService.ApplyPlatformMetadata(clip, "pregacao");

        var all = clip.PlatformMetadata.YouTube.Hashtags.Concat(clip.PlatformMetadata.Instagram.Hashtags).Concat(clip.PlatformMetadata.TikTok.Hashtags);
        Assert.DoesNotContain(all, tag => tag.Equals("#fyp", StringComparison.OrdinalIgnoreCase) || tag.Equals("#viral", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("#promessa", all);
    }

    [Theory]
    [InlineData(SocialPlatform.YouTube)]
    [InlineData(SocialPlatform.Instagram)]
    [InlineData(SocialPlatform.TikTok)]
    public void ForPlatform_RetornaTituloEDescricaoPersistiveis(SocialPlatform platform)
    {
        var clip = Clip(); ShortFormMetadataService.ApplyPlatformMetadata(clip, "pregacao");
        var metadata = ShortFormMetadataService.ForPlatform(clip, platform);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Title));
        Assert.False(string.IsNullOrWhiteSpace(metadata.Description));
    }

    private static ClipCandidate Clip() => new() { Title = "A promessa permanece mesmo quando tudo muda", CoverText = "A PROMESSA PERMANECE", HookSentence = "Você precisa lembrar desta promessa", Caption = "Há momentos em que a fé precisa permanecer firme.", Transcript = "A promessa de Deus permanece mesmo quando as circunstâncias mudam.", Hashtags = ["#promessa", "#fe"] };
}
