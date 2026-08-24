namespace CortaFeStudio.Api.Services;

public static class TikTokPublishingPolicy
{
    public static string PrivacyLevel(string privacy) => privacy.Trim().ToLowerInvariant() switch
    {
        "public" or "public_to_everyone" => "PUBLIC_TO_EVERYONE",
        "friends" or "mutual_follow_friends" => "MUTUAL_FOLLOW_FRIENDS",
        "followers" or "follower_of_creator" => "FOLLOWER_OF_CREATOR",
        "private" or "self_only" => "SELF_ONLY",
        _ => throw new InvalidOperationException("Privacidade inválida para o TikTok.")
    };
}
