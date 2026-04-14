# STS2TwitchVoteController
Allows twitch voting for game actions

## Setup

Create `%APPDATA%\SlayTheSpire2\TwitchVoteController.config.json` with the following:

```json
{
  "channel": "your_channel",
  "username": "your_username",
  "oauthToken": "oauth:your_token"
}
```

- **channel** — the Twitch channel to connect to
- **username** — the Twitch account that will read/send chat messages
- **oauthToken** — OAuth token for that account, prefixed with `oauth:`

Get your OAuth token from https://twitchapps.com/tmi/
