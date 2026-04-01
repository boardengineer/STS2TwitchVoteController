using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace STS2Twitch;

public static class MapOverlay
{
    private static readonly List<Label> _labels = new();

    private static readonly FieldInfo? MapPointDictionaryField =
        typeof(NMapScreen).GetField("_mapPointDictionary", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Refresh(NMapScreen screen)
    {
        ClearLabels();

        if (MapPointDictionaryField?.GetValue(screen)
            is not Dictionary<MapCoord, NMapPoint> dictionary)
            return;

        var travelable = dictionary.Values
            .Where(p => p.State == MapPointState.Travelable)
            .OrderBy(p => p.Point.coord.col)
            .ToList();

        for (int i = 0; i < travelable.Count; i++)
        {
            var point = travelable[i];
            var label = new Label();
            label.Text = (i + 1).ToString();
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeFontSizeOverride("font_size", 48);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 8);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Size = new Vector2(80, 80);
            label.Position = point.PivotOffset + new Vector2(-40, -80);
            label.ZIndex = 100;
            point.AddChild(label);
            _labels.Add(label);
        }
    }

    public static void ClearLabels()
    {
        foreach (var label in _labels)
        {
            if (GodotObject.IsInstanceValid(label))
            {
                label.QueueFree();
            }
        }
        _labels.Clear();
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
public class MapRecalculatePatch
{
    [HarmonyPostfix]
    public static void Postfix(NMapScreen __instance)
    {
        if (__instance.IsTravelEnabled)
            MapOverlay.Refresh(__instance);
        else
            MapOverlay.ClearLabels();
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetTravelEnabled))]
public class MapTravelEnabledPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMapScreen __instance, bool enabled)
    {
        if (!enabled)
            MapOverlay.ClearLabels();
    }
}
