using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using LightsOn.Scan;

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
    public static readonly Vector4 Title = new(0.98f, 0.78f, 0.32f, 1f);

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

    public static void DrawHere(string line, bool sameLine)
    {
        if (sameLine)
            ImGui.SameLine(0, 12);
        ImGui.TextDisabled(SeIconChar.LinkMarker.ToIconString());
        ImGui.SameLine(0, 4);
        ImGui.TextColored(Teal, line);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Where you are. Cross-world mark is people in range, not a zone census.");
    }

    public static Vector4 TierColor(string? tier) => NearbyScan.TierRank(tier) switch
    {
        5 => Happening,
        4 => Teal,
        3 => Amber,
        2 => Yellow,
        1 => Orange,
        _ => Mute,
    };

    public static Vector4 AgeColor(DateTimeOffset? at)
    {
        var mins = at is null ? 0 : Math.Max(0, (int)(DateTimeOffset.UtcNow - at.Value).TotalMinutes);
        if (mins < 20)
            return ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        if (mins < 60)
            return Mute;
        if (mins < 120)
            return Yellow;
        return new Vector4(0.55f, 0.56f, 0.58f, 1f);
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
