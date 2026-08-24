using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class TikTokPublishingPolicyTests
{
    [Theory]
    [InlineData("private", "SELF_ONLY")]
    [InlineData("SELF_ONLY", "SELF_ONLY")]
    [InlineData("public", "PUBLIC_TO_EVERYONE")]
    [InlineData("friends", "MUTUAL_FOLLOW_FRIENDS")]
    [InlineData("followers", "FOLLOWER_OF_CREATOR")]
    public void PrivacyLevel_ConverteOpcoesDaInterface(string input, string expected) =>
        Assert.Equal(expected, TikTokPublishingPolicy.PrivacyLevel(input));

    [Fact]
    public void PrivacyLevel_RejeitaOpcaoDesconhecida() =>
        Assert.Throws<InvalidOperationException>(() => TikTokPublishingPolicy.PrivacyLevel("qualquer"));
}
