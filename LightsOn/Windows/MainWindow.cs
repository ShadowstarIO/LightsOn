using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LightsOn.Api;
using LightsOn.Scan;

namespace LightsOn.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string query = "";
    private string dcPick = "";
    private string worldPick = "";
    private string dcFilter = "";
    private string worldFilter = "";
    private int filter;
    private string? selectedId;
    private static readonly string[] StatusFilters = ["All", "Lanterns lit", "Open now", "Vacant", "No data"];

    public MainWindow(Plugin plugin)
        : base($"LightsOn {Plugin.Version}###LightsOnMain")
    {
        this.plugin = plugin;
        Size = new Vector2(820, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 400),
            MaximumSize = new Vector2(1200, 980),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        if (!cfg.HasSeenWelcome)
        {
            ImGui.TextWrapped(Copy.Welcome);
            if (ImGui.Button("Got it"))
            {
                cfg.HasSeenWelcome = true;
                cfg.Save();
            }
            ImGui.Separator();
        }

        DrawPrivateAsk();

        if (ImGui.BeginTabBar("lo-tabs"))
        {
            if (ImGui.BeginTabItem("Venues"))
            {
                DrawVenues();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Outdoors"))
            {
                DrawOutdoors();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    public void Select(string id) => selectedId = id;

    private void DrawPrivateAsk()
    {
        var pending = plugin.Session.OutdoorPrivate;
        if (pending is null)
            return;
        ImGui.TextWrapped("Most of the company here looks like friends or Free Company. Is this a private gathering?");
        if (ImGui.SmallButton("Yes, keep it off the list"))
            _ = plugin.TryOutdoor(pending.Scan, true);
        ImGui.SameLine();
        if (ImGui.SmallButton("No, it's public"))
            _ = plugin.TryOutdoor(pending.Scan, false);
        ImGui.SameLine();
        if (ImGui.SmallButton("Skip"))
            plugin.Session.OutdoorPrivate = null;
        ImGui.Separator();
    }

    private void DrawVenues()
    {
        UiTheme.Section("Venues");
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.StatusLine);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
            _ = plugin.RefreshVenues(true);
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();

        var showOther = plugin.Configuration.ShowOtherRegions;
        var dcs = plugin.Venues
            .Select(v => v.Location?.DataCenter ?? "")
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => showOther || Reach.CanVisitDc(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var worlds = plugin.Venues
            .Where(v => dcPick.Length == 0
                        || string.Equals(v.Location?.DataCenter, dcPick, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Location?.World ?? "")
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => showOther || Reach.CanVisitWorld(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (worldPick.Length > 0 && !worlds.Contains(worldPick, StringComparer.OrdinalIgnoreCase))
            worldPick = "";

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##q", "Search name", ref query, 80);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        PlaceCombo("##dc", dcPick.Length == 0 ? "All data centers" : dcPick, dcs, "All data centers", ref dcFilter, ref dcPick);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        PlaceCombo("##world", worldPick.Length == 0 ? "All worlds" : worldPick, worlds, "All worlds", ref worldFilter, ref worldPick);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        ImGui.Combo("##status", ref filter, StatusFilters, StatusFilters.Length);
        ImGui.SameLine();
        if (ImGui.Checkbox("Other regions", ref showOther))
        {
            plugin.Configuration.ShowOtherRegions = showOther;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Worlds you cannot visit from this character. Off by default.");

        var rows = plugin.Venues
            .Where(Matches)
            .OrderBy(v => Reach.CanVisitWorld(v.Location?.World) ? 0 : 1)
            .ThenByDescending(v => v.Resolution?.IsNow == true)
            .ThenBy(v => v.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = rows.FirstOrDefault(v => v.Id == selectedId) ?? rows.FirstOrDefault();
        selectedId = selected?.Id;

        var listW = Math.Max(280, ImGui.GetContentRegionAvail().X * 0.46f);
        ImGui.BeginChild("list", new Vector2(listW, -1), true);
        var openRows = rows.Where(v => v.Resolution?.IsNow == true).ToList();
        var laterRows = rows.Where(v => v.Resolution?.IsNow != true).ToList();
        DrawVenueRows(openRows);
        if (openRows.Count > 0 && laterRows.Count > 0)
            ImGui.Separator();
        DrawVenueRows(laterRows);
        if (rows.Count == 0)
            ImGui.TextDisabled("No venues match.");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("detail", new Vector2(0, -1), true);
        if (selected is null)
            ImGui.TextDisabled("Pick a venue.");
        else
            VenueView.Draw(plugin, selected, false);
        ImGui.EndChild();
    }

    private void DrawVenueRows(List<VenueListing> rows)
    {
        foreach (var venue in rows)
        {
            var loc = venue.Location;
            var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
            var open = venue.Resolution?.IsNow == true;
            var mark = occ.IsHappening ? "● " : occ.IsWrappedUp ? "○ " : open ? "· " : "  ";
            var name = venue.Name ?? "";
            if (ImGui.Selectable($"{mark}{name}##{venue.Id}", venue.Id == selectedId))
                selectedId = venue.Id;
            if (open)
            {
                ImGui.SameLine();
                var (label, color) = VenueView.OpenBadge(occ);
                ImGui.TextColored(color, label);
            }
            if (loc is not null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(loc.Address);
            }
        }
    }

    private void DrawOutdoors()
    {
        UiTheme.Section("Outdoors");
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();
        ImGui.TextWrapped(Copy.OutdoorsHint);
        if (!plugin.Configuration.NoteOutdoorScenes)
            ImGui.TextDisabled("Settings → Note outdoor scenes to contribute. You can still read the list.");

        var rows = plugin.Outdoors
            .OrderBy(o => TierRank(o.Tier))
            .ThenByDescending(o => o.UpdatedAt)
            .ToList();
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No outdoor scenes noted in the last 20 minutes.");
            return;
        }

        string? world = null;
        foreach (var row in rows)
        {
            if (world != row.World)
            {
                world = row.World;
                UiTheme.Section(world, true);
            }
            ImGui.TextColored(UiTheme.Happening, NearbyScan.TierLabel(row.Tier));
            ImGui.SameLine();
            ImGui.TextWrapped(row.Place);
            if (row.InCharacter)
            {
                ImGui.SameLine();
                ImGui.TextColored(UiTheme.Teal, "in character");
            }
            ImGui.TextDisabled($"{row.Reports} note{(row.Reports == 1 ? "" : "s")} · {Age(row.UpdatedAt)}");
        }
    }

    private bool Matches(VenueListing venue)
    {
        if (venue is null)
            return false;
        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        if (filter == 1 && !occ.IsHappening)
            return false;
        if (filter == 2 && venue.Resolution?.IsNow != true)
            return false;
        if (filter == 3 && !occ.IsWrappedUp)
            return false;
        if (filter == 4 && occ.State is not "unknown" and not "")
            return false;
        if (!plugin.Configuration.ShowOtherRegions
            && !Reach.CanVisitWorld(venue.Location?.World))
            return false;
        if (dcPick.Length > 0
            && !string.Equals(venue.Location?.DataCenter, dcPick, StringComparison.OrdinalIgnoreCase))
            return false;
        if (worldPick.Length > 0
            && !string.Equals(venue.Location?.World, worldPick, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrEmpty(query))
            return true;
        var loc = venue.Location;
        return (venue.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.World ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.DataCenter ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.District ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void PlaceCombo(string id, string preview, List<string> items, string allLabel, ref string filter, ref string picked)
    {
        if (!ImGui.BeginCombo(id, preview))
            return;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter" + id, "Search", ref filter, 40);
        if (ImGui.Selectable(allLabel, picked.Length == 0))
            picked = "";
        ImGui.Separator();
        foreach (var item in items)
        {
            if (filter.Length > 0 && item.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (ImGui.Selectable(item, string.Equals(item, picked, StringComparison.OrdinalIgnoreCase)))
                picked = item;
        }
        ImGui.EndCombo();
    }

    private static int TierRank(string tier) => tier switch
    {
        "extremely_busy" => 0,
        "some_activity" => 1,
        "some_wandering" => 2,
        _ => 3,
    };

    private static string Age(DateTimeOffset? at)
    {
        if (at is null)
            return "unknown age";
        var mins = Math.Max(0, (int)(DateTimeOffset.UtcNow - at.Value).TotalMinutes);
        if (mins < 1)
            return "just now";
        if (mins < 60)
            return $"{mins}m ago";
        return $"{mins / 60}h ago";
    }
}
