using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public class VoteExecutioner
{
    private static readonly FieldInfo? ReplayActiveField =
        typeof(ReplayEngine).GetField("_replayActive", BindingFlags.Static | BindingFlags.NonPublic);

    private const double VoteDuration = 20.0;
    private const double SingleOptionDelay = 5.0;

    private TwitchIrcClient? _ircClient;
    private Timer? _voteTimer;
    private Timer? _displayTimer;
    private Label? _timerLabel;
    private List<ReplayCommand> _options = new();
    private readonly Dictionary<string, int> _votes = new();
    private bool _voteActive;
    public bool AwaitingMapMove { get; set; }
    public bool ShopOpened { get; set; }
    public bool AwaitingProceedAfterShop { get; set; }
    public enum ChestState { Closed, Opened, RelicTaken }
    public ChestState TreasureState { get; set; }

    public void Initialize(TwitchIrcClient client, Node timerParent)
    {
        _ircClient = client;

        _voteTimer = new Timer();
        _voteTimer.OneShot = true;
        _voteTimer.Timeout += OnVoteEnd;
        timerParent.AddChild(_voteTimer);

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
        timerParent.AddChild(_timerLabel);

        _displayTimer = new Timer();
        _displayTimer.WaitTime = 0.1;
        _displayTimer.Autostart = false;
        _displayTimer.Timeout += UpdateTimerDisplay;
        timerParent.AddChild(_displayTimer);
    }

    public void StartVote(List<ReplayCommand> commands)
    {
        _voteTimer?.Stop();
        _voteActive = false;
        _options = commands;
        _votes.Clear();

        if (_ircClient == null || _voteTimer == null || commands.Count == 0)
            return;

        var nonDiscardOptions = commands.Where(c => c is not DiscardPotionCommand).ToList();
        if (nonDiscardOptions.Count <= 1)
        {
            var autoCmd = nonDiscardOptions.Count == 1 ? nonDiscardOptions[0] : commands[0];
            var desc = CommandDescriber.Describe(autoCmd);
            _options = new List<ReplayCommand> { autoCmd };
            var message = $"Only option: {desc}. Executing in {SingleOptionDelay:0}s...";
            _ircClient.SendMessage(message);
            PlayerActionBuffer.LogMigrationWarning($"[TwitchVoteController] {message}");
            _voteTimer.WaitTime = SingleOptionDelay;
            _voteTimer.Start();
            _voteActive = true;
            ShowTimer();
            return;
        }

        var descriptions = commands.Select(c => CommandDescriber.Describe(c)).ToList();
        var indexed = descriptions.Select((d, i) => $"{i + 1}: {d}");
        var chatMessage = "Vote now! " + string.Join(", ", indexed);
        _ircClient.SendMessage(chatMessage);
        PlayerActionBuffer.LogMigrationWarning($"[TwitchVoteController] {chatMessage}");

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

        if (choice < 1 || choice > _options.Count)
            return;

        _votes[username] = choice;
    }

    private void OnVoteEnd()
    {
        if (!_voteActive)
            return;

        _voteActive = false;
        HideTimer();

        if (_options.Count == 0)
            return;

        int winnerIndex;
        int winnerVotes;

        if (_options.Count == 1)
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

        var winner = _options[winnerIndex];
        var desc = CommandDescriber.Describe(winner);

        var resultMsg = winnerVotes > 0
            ? $"Vote result: {desc} ({winnerVotes} votes)"
            : $"Executing: {desc}";
        _ircClient?.SendMessage(resultMsg);
        PlayerActionBuffer.LogMigrationWarning($"[TwitchVoteController] {resultMsg}");

        ReplayActiveField?.SetValue(null, true);
        ReplayDispatcher.GameSpeed = 1.0f;
        var result = winner.Execute();
        ReplayDispatcher.ClearDispatchableCache();

        if (winner is ProceedToMapCommand)
            AwaitingMapMove = true;
        else if (winner is MapMoveCommand)
        {
            AwaitingMapMove = false;
            ShopOpened = false;
            TreasureState = ChestState.Closed;
        }

        if (winner is OpenShopCommand or OpenFakeShopCommand)
            ShopOpened = true;
        else if (winner is CloseShopCommand)
        {
            ShopOpened = false;
            AwaitingProceedAfterShop = true;
        }

        if (winner is OpenChestCommand)
            TreasureState = ChestState.Opened;
        else if (winner is TakeChestRelicCommand)
            TreasureState = ChestState.RelicTaken;

        PlayerActionBuffer.LogMigrationWarning($"[TwitchVoteController] Executed: {winner} (success={result.Success})");
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
        CardVoteOverlay.ClearLabels();
        SelectionOverlay.ClearLabels();
        EventOverlay.ClearLabels();
        MapOverlay.ClearLabels();
        ShopOverlay.ClearLabels();
    }

    private void UpdateTimerDisplay()
    {
        if (_timerLabel == null || _voteTimer == null)
            return;

        var remaining = Math.Ceiling(_voteTimer.TimeLeft);
        _timerLabel.Text = $"Vote: {remaining:0}s";

        if (_voteActive)
        {
            if (_options.Any(o => o is PlayCardCommand or SelectHandCardsCommand))
                CardVoteOverlay.Refresh(_options, _votes);

            if (_options.Any(o => o is TakeCardCommand or ClaimRewardCommand or ChooseRestSiteOptionCommand
                    or SelectCardFromScreenCommand or SelectGridCardCommand))
                SelectionOverlay.Refresh(_options, _votes);

            if (_options.Any(o => o is ChooseEventOptionCommand))
                EventOverlay.Refresh(_options, _votes);

            if (_options.Any(o => o is BuyCardCommand or BuyRelicCommand
                    or BuyPotionCommand or BuyCardRemovalCommand
                    or OpenShopCommand or OpenFakeShopCommand or CloseShopCommand)
                || (_options.Any(o => o is ProceedToMapCommand) && ReplayState.ActiveMerchantRoom != null))
                ShopOverlay.Refresh(_options, _votes);

            if (_options.Any(o => o is MapMoveCommand))
            {
                var screen = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
                if (screen != null)
                {
                    var tally = new Dictionary<int, int>();
                    foreach (var choice in _votes.Values)
                    {
                        tally.TryGetValue(choice, out var count);
                        tally[choice] = count + 1;
                    }
                    MapOverlay.RefreshWithVotes(screen, _options, tally);
                }
            }
        }
    }
}
