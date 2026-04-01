using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunReplays;

namespace STS2Twitch;

public class TwitchConfig
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("oauthToken")]
    public string OauthToken { get; set; } = "";

    public static TwitchConfig? Load(string path)
    {
        if (!File.Exists(path))
        {
            PlayerActionBuffer.LogMigrationWarning(
                $"[TwitchVoteController] Config file not found at: {path}\n" +
                "[TwitchVoteController] Create TwitchVoteController.config.json with:\n" +
                "[TwitchVoteController] {\"channel\": \"your_channel\", \"username\": \"your_username\", \"oauthToken\": \"oauth:your_token\"}\n" +
                "[TwitchVoteController] Get your OAuth token from https://twitchapps.com/tmi/");
            return null;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<TwitchConfig>(json);

        if (config == null || string.IsNullOrWhiteSpace(config.Channel) ||
            string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.OauthToken))
        {
            PlayerActionBuffer.LogMigrationWarning("[TwitchVoteController] Config file is missing required fields (channel, username, oauthToken).");
            return null;
        }

        return config;
    }
}
