using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using RunReplays;
using STS2Twitch.Overlays;
namespace STS2Twitch;

public class CharacterVoteController
{
    private const double VoteDuration = 10.0;

    private TwitchIrcClient? _ircClient;
    private NGame? _gameInstance;
    private Timer? _voteTimer;
    private Timer? _displayTimer;
    private Label? _timerLabel;
    private readonly Dictionary<string, int> _votes = new();
    private bool _voteActive;
    private List<CharacterModel> _characters = new();
    private NCharacterSelectScreen? _charSelectScreen;
    private int _displayedLeaderIndex = -1;

    public bool IsVoteActive => _voteActive;

    public void Initialize(TwitchIrcClient client, NGame game)
    {
        _ircClient = client;
        _gameInstance = game;

        _voteTimer = new Timer();
        _voteTimer.OneShot = true;
        _voteTimer.Timeout += OnVoteEnd;
        game.AddChild(_voteTimer);

        _timerLabel = new Label();
        _timerLabel.AddThemeColorOverride("font_color", Colors.White);
        _timerLabel.AddThemeFontSizeOverride("font_size", 36);
        _timerLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _timerLabel.AddThemeConstantOverride("outline_size", 6);
        _timerLabel.AnchorLeft = 0;
        _timerLabel.AnchorTop = 0.2f;
        _timerLabel.AnchorRight = 0;
        _timerLabel.AnchorBottom = 0.2f;
        _timerLabel.OffsetLeft = 20;
        _timerLabel.OffsetTop = 0;
        _timerLabel.Visible = false;
        game.AddChild(_timerLabel);

        _displayTimer = new Timer();
        _displayTimer.WaitTime = 0.1;
        _displayTimer.Autostart = false;
        _displayTimer.Timeout += UpdateTimerDisplay;
        game.AddChild(_displayTimer);
    }

    public void StartCharacterVote()
    {
        if (_voteActive || _gameInstance == null || _ircClient == null)
            return;

        NavigateToCharacterSelect();

        _characters = ModelDb.AllCharacters
            .Where(c => c is not RandomCharacter)
            .ToList();

        if (_characters.Count == 0)
        {
            PlayerActionBuffer.LogMigrationWarning("[CharacterVote] No characters found.");
            return;
        }

        PlayerActionBuffer.LogMigrationWarning($"[CharacterVote] Found {_characters.Count} characters: {string.Join(", ", _characters.Select(c => c.Id.Entry))}");
        BeginVote();
    }

    private void NavigateToCharacterSelect()
    {
        var mainMenu = NGame.Instance?.MainMenu;
        if (mainMenu == null)
        {
            PlayerActionBuffer.LogMigrationWarning("[CharacterVote] Not at main menu, skipping navigation to character select.");
            return;
        }

        _charSelectScreen = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        _charSelectScreen.InitializeSingleplayer();
        mainMenu.SubmenuStack.Push(_charSelectScreen);
        PlayerActionBuffer.LogMigrationWarning("[CharacterVote] Navigated to character select screen.");
    }

    private void BeginVote()
    {
        if (_ircClient == null || _voteTimer == null)
            return;

        _votes.Clear();

        if (_characters.Count == 1)
        {
            var message = $"Only character: {_characters[0].Id.Entry}. Selecting in 5s...";
            _ircClient.SendMessage(message);
            PlayerActionBuffer.LogMigrationWarning($"[CharacterVote] {message}");
            _voteTimer.WaitTime = 5.0;
            _voteTimer.Start();
            _voteActive = true;
            ShowTimer();
            return;
        }

        var indexed = _characters.Select((c, i) => $"{i + 1}: {c.Id.Entry}");
        var chatMessage = "Vote for character! " + string.Join(", ", indexed);
        _ircClient.SendMessage(chatMessage);
        PlayerActionBuffer.LogMigrationWarning($"[CharacterVote] {chatMessage}");

        _voteTimer.WaitTime = VoteDuration;
        _voteTimer.Start();
        _voteActive = true;
        ShowTimer();
    }

    public void OnChatMessage(string username, string message)
    {
        if (!_voteActive)
            return;

        var trimmed = message.Trim();
        if (!int.TryParse(trimmed, out var choice))
            return;

        if (choice < 1 || choice > _characters.Count)
            return;

        _votes[username] = choice;
    }

    private void OnVoteEnd()
    {
        if (!_voteActive)
            return;

        _voteActive = false;
        HideTimer();

        if (_characters.Count == 0)
            return;

        int winnerIndex;
        int winnerVotes;

        if (_characters.Count == 1)
        {
            winnerIndex = 0;
            winnerVotes = 0;
        }
        else
        {
            var tally = new Dictionary<int, int>();
            foreach (var choice in _votes.Values)
            {
                tally.TryGetValue(choice, out var count);
                tally[choice] = count + 1;
            }

            if (tally.Count == 0)
            {
                winnerIndex = 0;
                winnerVotes = 0;
            }
            else
            {
                var maxVotes = tally.Values.Max();
                winnerIndex = tally
                    .Where(kv => kv.Value == maxVotes)
                    .Min(kv => kv.Key) - 1;
                winnerVotes = maxVotes;
            }
        }

        var winner = _characters[winnerIndex];
        var winnerName = winner.Id.Entry;
        var resultMsg = winnerVotes > 0
            ? $"Vote result: {winnerName} ({winnerVotes} votes)"
            : $"Selecting: {winnerName}";
        _ircClient?.SendMessage(resultMsg);
        PlayerActionBuffer.LogMigrationWarning($"[CharacterVote] {resultMsg}");

        StartRunWithCharacter(winner);
    }

    private async void StartRunWithCharacter(CharacterModel character)
    {
        if (_gameInstance == null)
            return;

        var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(character.Id);
        var ascension = stats.MaxAscension;

        var seed = SeedHelper.GetRandomSeed();
        var acts = ActModel.GetDefaultList();

        PlayerActionBuffer.LogMigrationWarning(
            $"[CharacterVote] Starting run: {character.Id.Entry}, Ascension {ascension}, Seed {seed}");

        try
        {
            await _gameInstance.StartNewSingleplayerRun(
                character, shouldSave: true, acts, Array.Empty<ModifierModel>(), seed, ascension);
        }
        catch (Exception ex)
        {
            PlayerActionBuffer.LogMigrationWarning($"[CharacterVote] Failed to start run: {ex}");
        }
    }

    private void ShowTimer()
    {
        if (_timerLabel != null)
            _timerLabel.Visible = true;
        _displayTimer?.Start();
        UpdateTimerDisplay();
    }

    private void HideTimer()
    {
        _displayTimer?.Stop();
        if (_timerLabel != null)
            _timerLabel.Visible = false;
        CharacterVoteOverlay.ClearLabels();
        _charSelectScreen = null;
        _displayedLeaderIndex = -1;
    }

    private void UpdateTimerDisplay()
    {
        if (_timerLabel == null || _voteTimer == null)
            return;

        var remaining = Math.Ceiling(_voteTimer.TimeLeft);
        _timerLabel.Text = $"Vote: {remaining:0}s";

        if (_charSelectScreen != null && GodotObject.IsInstanceValid(_charSelectScreen))
        {
            CharacterVoteOverlay.Refresh(_charSelectScreen, _characters, _votes);
            UpdateLeaderSelection();
        }
    }

    private void UpdateLeaderSelection()
    {
        if (_charSelectScreen == null || _votes.Count == 0)
            return;

        var tally = new Dictionary<int, int>();
        foreach (var choice in _votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        var maxVotes = tally.Values.Max();
        var leaderIndex = tally
            .Where(kv => kv.Value == maxVotes)
            .Min(kv => kv.Key) - 1;

        if (leaderIndex == _displayedLeaderIndex)
            return;

        _displayedLeaderIndex = leaderIndex;
        var button = CharacterVoteOverlay.FindButton(_charSelectScreen, _characters[leaderIndex]);
        button?.Select();
    }
}
