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

        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        // Overlay on room-level buttons (open shop, proceed, close shop)
        var backButton = room.Inventory?.GetNodeOrNull<Control>("%BackButton");

        for (int i = 0; i < options.Count; i++)
        {
            Control? target = null;

            if (options[i] is OpenShopCommand or OpenFakeShopCommand)
                target = room.MerchantButton;
            else if (options[i] is ProceedToMapCommand)
                target = room.ProceedButton;
            else if (options[i] is CloseShopCommand)
                target = backButton;

            if (target != null && GodotObject.IsInstanceValid(target))
            {
                tally.TryGetValue(i + 1, out var voteCount);
                AddLabel(target, i + 1, voteCount, new Vector2(target.Size.X / 2 - 40, -10));
            }
        }

        // Buy state: overlay on shop slots
        var inventory = room.Inventory;
        if (inventory == null)
            return;

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

            tally.TryGetValue(i + 1, out var vc);
            AddLabel(matchedSlot, i + 1, vc, new Vector2(matchedSlot.Size.X / 2 - 40, -10));
        }
    }

    private static void AddLabel(Control parent, int optionNumber, int voteCount, Vector2 offset)
    {
        var label = new Label();
        label.Text = voteCount > 0 ? $"[{optionNumber}]:{voteCount}" : $"[{optionNumber}]";
        label.AddThemeColorOverride("font_color", Colors.Yellow);
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Size = new Vector2(80, 40);
        label.Position = offset;
        label.ZIndex = 100;
        parent.AddChild(label);
        _labels.Add(label);
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
