using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace LightsOn.Windows;

internal static class UiTheme
{
    public static readonly Vector4 Amber = new(0.93f, 0.62f, 0.15f, 1f);
    public static readonly Vector4 Teal = new(0.11f, 0.70f, 0.69f, 1f);
    public static readonly Vector4 Mute = new(0.62f, 0.64f, 0.68f, 1f);
    public static readonly Vector4 Happening = new(0.45f, 0.82f, 0.48f, 1f);
    public static readonly Vector4 Wrapped = new(0.75f, 0.70f, 0.55f, 1f);
    public static readonly Vector4 Yellow = new(0.97f, 0.88f, 0.22f, 1f);
    public static readonly Vector4 Orange = new(0.96f, 0.42f, 0.12f, 1f);

    public static void Section(string label, bool action = false)
        => ImGui.TextColored(action ? Amber : Teal, label);

    public static void Gap() => ImGui.Dummy(new Vector2(0, 8));

    public static void Hint(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(i)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    public static bool SearchCombo(string id, ref int current, string[] items, ref string filter)
    {
        var preview = items.Length == 0 ? "" : items[Math.Clamp(current, 0, items.Length - 1)];
        if (!ImGui.BeginCombo(id, preview))
            return false;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##f" + id, "Search", ref filter, 40);
        var changed = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (filter.Length > 0 && items[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (ImGui.Selectable(items[i], i == current))
            {
                current = i;
                changed = true;
            }
        }
        ImGui.EndCombo();
        return changed;
    }
}
