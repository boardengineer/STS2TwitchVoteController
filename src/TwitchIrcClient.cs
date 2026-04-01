using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace STS2Twitch;

public class TwitchIrcClient
{
    private const string TwitchIrcHost = "irc.chat.twitch.tv";
    private const int TwitchIrcPort = 6667;
    private const int MaxReconnectDelay = 60;

    private readonly string _channel;
    private readonly string _username;
    private readonly string _oauthToken;

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public event Action<string, string>? OnMessageReceived;

    public TwitchIrcClient(TwitchConfig config)
    {
        _channel = config.Channel.ToLowerInvariant();
        _username = config.Username.ToLowerInvariant();
        _oauthToken = config.OauthToken;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => RunAsync(_cts.Token));
    }

    public void SendMessage(string message)
    {
        var writer = _writer;
        if (writer == null)
            return;

        Task.Run(async () =>
        {
            try
            {
                await writer.WriteLineAsync($"PRIVMSG #{_channel} :{message}");
            }
            catch (Exception ex)
            {
                DevConsoleLogger.Enqueue($"[TwitchVoteController] Failed to send message: {ex.Message}");
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        Cleanup();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var reconnectDelay = 5;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                DevConsoleLogger.Enqueue($"[TwitchVoteController] Connecting to Twitch IRC (#{_channel})...");

                _client = new TcpClient();
                await _client.ConnectAsync(TwitchIrcHost, TwitchIrcPort, ct);

                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };

                await _writer.WriteLineAsync($"PASS {_oauthToken}");
                await _writer.WriteLineAsync($"NICK {_username}");
                await _writer.WriteLineAsync($"JOIN #{_channel}");

                reconnectDelay = 5;
                DevConsoleLogger.Enqueue($"[TwitchVoteController] Connected to #{_channel}!");

                await ReadLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DevConsoleLogger.Enqueue($"[TwitchVoteController] Connection error: {ex.Message}");
                Cleanup();

                if (ct.IsCancellationRequested)
                    break;

                DevConsoleLogger.Enqueue($"[TwitchVoteController] Reconnecting in {reconnectDelay}s...");
                try
                {
                    await Task.Delay(reconnectDelay * 1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                reconnectDelay = Math.Min(reconnectDelay * 2, MaxReconnectDelay);
            }
        }

        Cleanup();
        DevConsoleLogger.Enqueue("[TwitchVoteController] Disconnected from Twitch IRC.");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _reader != null)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line == null)
                break;

            if (line.StartsWith("PING"))
            {
                await _writer!.WriteLineAsync("PONG :tmi.twitch.tv");
                continue;
            }

            if (line.Contains("Login authentication failed"))
            {
                DevConsoleLogger.Enqueue("[TwitchVoteController] Authentication failed. Check your OAuth token.");
                throw new InvalidOperationException("Auth failed");
            }

            var parsed = ParsePrivMsg(line);
            if (parsed != null)
            {
                OnMessageReceived?.Invoke(parsed.Value.Username, parsed.Value.Message);
            }
        }
    }

    private static (string Username, string Message)? ParsePrivMsg(string line)
    {
        // Format: :username!username@username.tmi.twitch.tv PRIVMSG #channel :message
        if (!line.Contains("PRIVMSG"))
            return null;

        var exclamation = line.IndexOf('!');
        if (exclamation < 2)
            return null;

        var username = line.Substring(1, exclamation - 1);

        var msgStart = line.IndexOf(':', 1);
        if (msgStart < 0)
            return null;

        // Find the second colon after PRIVMSG #channel
        var privmsgIdx = line.IndexOf("PRIVMSG", StringComparison.Ordinal);
        if (privmsgIdx < 0)
            return null;

        var messageColon = line.IndexOf(':', privmsgIdx);
        if (messageColon < 0)
            return null;

        var message = line.Substring(messageColon + 1);
        return (username, message);
    }

    private void Cleanup()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }
}
