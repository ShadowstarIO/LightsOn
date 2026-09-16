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
    private int venueFilter;
    private int outdoorFilter;
    private string? selectedId;
    private string? selectedZone;
    private static readonly string[] StatusFilters = ["All", "Lanterns Lit", "Open Now", "Vacant", "No Data"];
    private static readonly string[] OutdoorFilters = ["All", "Extremely Busy", "Some Activity", "Some Wandering"];

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
        if (ImGui.SmallButton("Yes, Private"))
            _ = plugin.TryOutdoor(pending.Scan, true);
        ImGui.SameLine();
        if (ImGui.SmallButton("No, Public"))
            _ = plugin.TryOutdoor(pending.Scan, false);
        ImGui.SameLine();
        if (ImGui.SmallButton("Skip"))
            plugin.Session.OutdoorPrivate = null;
        ImGui.Separator();
    }

    private void DrawVenues()
    {
        ImGui.TextDisabled(plugin.StatusLine);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
            _ = plugin.RefreshVenues(true);
        ImGui.SameLine();
        if (ImGui.SmallButton("Directory"))
        {
            try { Dalamud.Utility.Util.OpenLink(Copy.DirectoryUrl); }
            catch (Exception ex) { Plugin.Log.Verbose(ex, "Open link"); }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();
        UiTheme.DrawHere(plugin.Session.HereLine, true);

        DrawPlaceFilters(true);

        var rows = plugin.Venues
            .Where(MatchesVenue)
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
        {
            UiTheme.Gap();
            ImGui.Separator();
        }
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
            var dot = open ? VenueView.BadgeColor(occ) : UiTheme.Mute;
            ImGui.TextColored(dot, "·");
            ImGui.SameLine(0, 6);
            var name = venue.Name ?? "";
            if (ImGui.Selectable($"{name}##{venue.Id}", venue.Id == selectedId))
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
        DrawOutdoorScanBar();
        DrawPlaceFilters(false);

        var rows = plugin.Outdoors
            .Where(MatchesOutdoor)
            .ToList();
        var zones = rows
            .GroupBy(o => ZoneKey(o))
            .Select(g => new OutdoorZone(
                g.Key,
                g.Max(o => NearbyScan.TierRank(o.Tier)),
                g.Count(),
                g.Max(o => o.UpdatedAt ?? DateTimeOffset.MinValue),
                g.OrderByDescending(o => NearbyScan.TierRank(o.Tier)).ThenByDescending(o => o.UpdatedAt).ToList()))
            .OrderByDescending(z => z.Rank)
            .ThenByDescending(z => z.UpdatedAt)
            .ToList();

        if (selectedZone is not null && zones.All(z => z.Key != selectedZone))
            selectedZone = null;
        selectedZone ??= zones.FirstOrDefault()?.Key;
        var picked = zones.FirstOrDefault(z => z.Key == selectedZone);

        var listW = Math.Max(260, ImGui.GetContentRegionAvail().X * 0.42f);
        ImGui.BeginChild("oz-list", new Vector2(listW, -1), true);
        if (zones.Count == 0)
            ImGui.TextDisabled($"No outdoor scenes noted in the last {Limits.OutdoorListMinutes} minutes.");
        foreach (var zone in zones)
        {
            var top = zone.Pockets[0];
            ImGui.TextColored(UiTheme.Happening, "·");
            ImGui.SameLine(0, 6);
            if (ImGui.Selectable($"{zone.Key}##{zone.Key}", zone.Key == selectedZone))
                selectedZone = zone.Key;
            ImGui.SameLine();
            ImGui.TextColored(UiTheme.Happening, NearbyScan.TierLabel(top.Tier));
            ImGui.TextDisabled($"{zone.Count} spot{(zone.Count == 1 ? "" : "s")} · {VenueView.Age(zone.UpdatedAt)}");
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("oz-detail", new Vector2(0, -1), true);
        if (picked is null)
            ImGui.TextDisabled("Pick a zone.");
        else
        {
            ImGui.TextColored(UiTheme.Title, picked.Key);
            ImGui.TextDisabled(Copy.OutdoorsHint);
            foreach (var row in picked.Pockets)
            {
                UiTheme.Gap();
                ImGui.Separator();
                var here = string.Equals(row.Pocket, plugin.Session.PocketKey, StringComparison.Ordinal);
                ImGui.TextColored(UiTheme.Happening, NearbyScan.TierLabel(row.Tier));
                ImGui.SameLine();
                var coords = NearbyScan.PocketCoords(row.Pocket);
                ImGui.TextWrapped(string.IsNullOrEmpty(coords) ? row.Place : $"{row.Place} {coords}");
                if (here)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(UiTheme.Amber, "here");
                }
                if (row.InCharacter)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(UiTheme.Teal, "IC");
                }
                ImGui.TextColored(UiTheme.AgeColor(row.UpdatedAt),
                    $"{row.Reports} note{(row.Reports == 1 ? "" : "s")} · {VenueView.Age(row.UpdatedAt)}");
                if (ImGui.SmallButton($"Flag##{row.Pocket}"))
                    Here.FlagPocket(row.Pocket);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Place a map flag on this pocket. Coarse cell, not a person's feet.");
            }
        }
        ImGui.EndChild();
    }

    private void DrawOutdoorScanBar()
    {
        var onPlot = plugin.Session.PlotKey.Length > 0;
        var watching = plugin.Session.Watching;
        var ready = plugin.Session.WatchReady;
        var canNote = plugin.Configuration.NoteOutdoorScenes && plugin.CanSend;

        if (watching)
        {
            if (ImGui.SmallButton("Cancel"))
                plugin.CancelOutdoorWatch();
        }
        else
        {
            var blocked = onPlot;
            if (blocked)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton("Audit"))
                plugin.StartOutdoorWatch();
            if (blocked)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(onPlot
                    ? "Outdoor notes are for the street, not plots."
                    : "Stay in this area. About a minute; busier scenes finish sooner.");
            }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
            _ = plugin.RefreshOutdoors();
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();
        UiTheme.DrawHere(plugin.Session.HereLine, true);

        if (!plugin.Configuration.NoteOutdoorScenes)
            ImGui.TextDisabled("Settings → Note Outdoor Scenes to contribute. You can still read the list.");

        if (watching)
        {
            ImGui.TextWrapped(plugin.Session.WatchLine);
            if (ready)
            {
                var peak = plugin.Session.WatchPeak;
                var wait = plugin.Session.OutdoorWait(peak.Pocket, peak.Tier);
                var noteOk = canNote && peak.Tier.Length > 0 && wait <= TimeSpan.Zero;
                if (!noteOk)
                    ImGui.BeginDisabled();
                var label = wait > TimeSpan.Zero
                    ? $"Note ({Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))}s)"
                    : "Note";
                if (ImGui.SmallButton(label))
                    _ = plugin.FinishOutdoorWatch();
                if (!noteOk)
                    ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.SmallButton("Skip"))
                    plugin.CancelOutdoorWatch();
            }
        }
        else if (plugin.Session.OutdoorLine.Length > 0)
            ImGui.TextWrapped(plugin.Session.OutdoorLine);
    }

    private void DrawPlaceFilters(bool venues)
    {
        var showOther = plugin.Configuration.ShowOtherRegions;
        var dcs = plugin.Venues
            .Select(v => v.Location?.DataCenter ?? "")
            .Concat(plugin.Outdoors.Select(o => Reach.DataCenterOf(o.World)))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => showOther || Reach.CanVisitDc(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var worlds = plugin.Venues
            .Select(v => new { Dc = v.Location?.DataCenter ?? "", World = v.Location?.World ?? "" })
            .Concat(plugin.Outdoors.Select(o => new { Dc = Reach.DataCenterOf(o.World), World = o.World }))
            .Where(x => x.World.Length > 0)
            .Where(x => dcPick.Length == 0 || string.Equals(x.Dc, dcPick, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.World)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => showOther || Reach.CanVisitWorld(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (worldPick.Length > 0 && !worlds.Contains(worldPick, StringComparer.OrdinalIgnoreCase))
            worldPick = "";

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##q", venues ? "Search Name" : "Search Place", ref query, 80);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        PlaceCombo("##dc", dcPick.Length == 0 ? "All Data Centers" : dcPick, dcs, "All Data Centers", ref dcFilter, ref dcPick);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        PlaceCombo("##world", worldPick.Length == 0 ? "All Worlds" : worldPick, worlds, "All Worlds", ref worldFilter, ref worldPick);
        if (worldPick.Length > 0 && dcPick.Length == 0)
        {
            var dc = Reach.DataCenterOf(worldPick);
            if (string.IsNullOrEmpty(dc))
            {
                dc = plugin.Venues.FirstOrDefault(v =>
                    string.Equals(v.Location?.World, worldPick, StringComparison.OrdinalIgnoreCase))?.Location?.DataCenter;
            }
            if (!string.IsNullOrEmpty(dc))
                dcPick = dc;
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        if (venues)
            ImGui.Combo("##status", ref venueFilter, StatusFilters, StatusFilters.Length);
        else
            ImGui.Combo("##ostatus", ref outdoorFilter, OutdoorFilters, OutdoorFilters.Length);
        ImGui.SameLine();
        if (ImGui.Checkbox("Other Regions", ref showOther))
        {
            plugin.Configuration.ShowOtherRegions = showOther;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Worlds you cannot visit from this character. Off by default.");
    }

    private bool MatchesVenue(VenueListing venue)
    {
        if (venue is null)
            return false;
        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        if (venueFilter == 1 && !occ.IsHappening)
            return false;
        if (venueFilter == 2 && venue.Resolution?.IsNow != true)
            return false;
        if (venueFilter == 3 && !occ.IsWrappedUp)
            return false;
        if (venueFilter == 4 && occ.State is not "unknown" and not "")
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

    private bool MatchesOutdoor(OutdoorSnapshot row)
    {
        if (!plugin.Configuration.ShowOtherRegions && !Reach.CanVisitWorld(row.World))
            return false;
        if (worldPick.Length > 0
            && !string.Equals(row.World, worldPick, StringComparison.OrdinalIgnoreCase))
            return false;
        if (dcPick.Length > 0
            && !string.Equals(Reach.DataCenterOf(row.World), dcPick, StringComparison.OrdinalIgnoreCase))
            return false;
        if (outdoorFilter == 1 && row.Tier != "extremely_busy")
            return false;
        if (outdoorFilter == 2 && row.Tier != "some_activity")
            return false;
        if (outdoorFilter == 3 && row.Tier != "some_wandering")
            return false;
        if (string.IsNullOrEmpty(query))
            return true;
        return row.Place.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.World.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string ZoneKey(OutdoorSnapshot row) => $"{row.World} · {row.Place}";

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

    private sealed record OutdoorZone(
        string Key,
        int Rank,
        int Count,
        DateTimeOffset UpdatedAt,
        List<OutdoorSnapshot> Pockets);
}
