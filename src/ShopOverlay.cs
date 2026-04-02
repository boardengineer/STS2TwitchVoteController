using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public static class ShopOverlay
{
    private static readonly List<Label> _labels = new();

    public static void Refresh(List<ReplayCommand> options, Dictionary<string, int> votes)
    {
        ClearLabels();

        var room = ReplayState.ActiveMerchantRoom;
        if (room == null || !GodotObject.IsInstanceValid(room))
            return;

        var inventory = room.Inventory;
        if (inventory == null)
            return;

        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        var slots = inventory.GetAllSlots().ToList();

        for (int i = 0; i < options.Count; i++)
        {
            NMerchantSlot? matchedSlot = null;

            switch (options[i])
            {
                case BuyCardCommand buyCard:
                    matchedSlot = slots.OfType<NMerchantCard>().FirstOrDefault(s =>
                        s.Entry is MerchantCardEntry cardEntry &&
                        cardEntry.CreationResult?.Card?.Title == buyCard.CardTitle);
                    break;

                case BuyRelicCommand buyRelic:
                    matchedSlot = slots.OfType<NMerchantRelic>().FirstOrDefault(s =>
                        s.Entry is MerchantRelicEntry relicEntry &&
                        relicEntry.Model?.Title.GetFormattedText() == buyRelic.RelicTitle);
                    break;

                case BuyPotionCommand buyPotion:
                    matchedSlot = slots.OfType<NMerchantPotion>().FirstOrDefault(s =>
                        s.Entry is MerchantPotionEntry potionEntry &&
                        potionEntry.Model?.Title.GetFormattedText() == buyPotion.PotionTitle);
                    break;

                case BuyCardRemovalCommand:
                    matchedSlot = slots.OfType<NMerchantCardRemoval>().FirstOrDefault();
                    break;
            }

            if (matchedSlot == null || !GodotObject.IsInstanceValid(matchedSlot))
                continue;

            tally.TryGetValue(i + 1, out var voteCount);

            var label = new Label();
            label.Text = voteCount > 0 ? $"[{i + 1}]:{voteCount}" : $"[{i + 1}]";
            label.AddThemeColorOverride("font_color", Colors.Yellow);
            label.AddThemeFontSizeOverride("font_size", 28);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Size = new Vector2(80, 40);
            label.Position = new Vector2(-40 + matchedSlot.Size.X / 2, -10);
            label.ZIndex = 100;
            matchedSlot.AddChild(label);
            _labels.Add(label);
        }
    }

    public static void ClearLabels()
    {
        foreach (var label in _labels)
        {
            if (GodotObject.IsInstanceValid(label))
                label.QueueFree();
        }
        _labels.Clear();
    }
}
