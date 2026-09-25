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
    private string outdoorSceneFilter = "";
    private string? selectedId;
    private string? selectedZone;
    private static readonly string[] StatusFilters = ["All", "Lanterns Lit", "Open Now", "Vacant", "No Data"];
    private static readonly string[] OutdoorFilters =
        ["All", "Extremely Busy", "Busy", "Some Activity", "Light Activity", "Some Wandering"];

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
        ImGui.AlignTextToFramePadding();
        ImGui.TextWrapped("Mostly friends/FC here. Private gathering?");
        ImGui.SameLine();
        if (ImGui.SmallButton("Yes"))
            _ = plugin.TryOutdoor(pending.Scan, true);
        ImGui.SameLine();
        if (ImGui.SmallButton("No"))
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
            .Select(g =>
            {
                var top = g.OrderByDescending(o => NearbyScan.TierRank(o.Tier)).ThenByDescending(o => o.UpdatedAt).First();
                return new OutdoorZone(
                    g.Key,
                    top.World,
                    string.IsNullOrWhiteSpace(top.Zone) ? top.Place : top.Zone,
                    top.Place,
                    NearbyScan.TierRank(top.Tier),
                    top.Tier,
                    g.Count(),
                    g.Max(o => o.UpdatedAt ?? DateTimeOffset.MinValue),
                    g.OrderByDescending(o => o.UpdatedAt).ToList());
            })
            .OrderByDescending(z => z.Rank)
            .ThenByDescending(z => z.UpdatedAt)
            .ThenByDescending(z => z.Count)
            .ToList();

        if (selectedZone is not null && zones.All(z => z.Key != selectedZone))
            selectedZone = null;
        selectedZone ??= zones.FirstOrDefault()?.Key;
        var picked = zones.FirstOrDefault(z => z.Key == selectedZone);

        var pins = PartakePinsForList();
        var bottom = pins.Count > 0 ? 148f : 0f;
        var listW = Math.Max(240, ImGui.GetContentRegionAvail().X * 0.40f);
        ImGui.BeginChild("oz-list", new Vector2(listW, -bottom), true);
        if (zones.Count == 0)
            ImGui.TextDisabled($"No outdoor scenes reported in the last {Limits.OutdoorListHours} hours.");
        foreach (var zone in zones)
        {
            ImGui.TextColored(UiTheme.TierColor(zone.Tier), "·");
            ImGui.SameLine(0, 6);
            var label = OutdoorTitle(zone);
            if (ImGui.Selectable($"{label}##{zone.Key}", zone.Key == selectedZone))
                selectedZone = zone.Key;
            ImGui.SameLine();
            ImGui.TextColored(UiTheme.TierColor(zone.Tier), NearbyScan.TierLabel(zone.Tier));
            ImGui.SameLine();
            ImGui.TextDisabled($"{zone.Count}");
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("oz-detail", new Vector2(0, -bottom), true);
        if (picked is null)
            ImGui.TextDisabled("Pick a zone.");
        else
            DrawOutdoorDetail(picked);
        ImGui.EndChild();
        if (pins.Count > 0)
            DrawPartakePins(pins);
    }

    private void DrawOutdoorDetail(OutdoorZone picked)
    {
        var top = picked.Reports[0];
        NearbyScan.TryParsePocket(top.Pocket, out _, out var territory, out _, out _);
        var (_, _, kind) = Zone.Describe(territory);
        ImGui.TextColored(UiTheme.Title, OutdoorTitle(picked));
        if (kind.Length > 0)
            ImGui.TextDisabled(kind);
        var pin = NearbyPartake(picked);
        if (pin is not null)
        {
            ImGui.TextDisabled(pin.Name);
            ImGui.SameLine();
            if (ImGui.SmallButton("ListingP##oz"))
                OpenPartake(pin.Url);
        }
        ImGui.Separator();
        foreach (var row in picked.Reports)
            DrawOutdoorReport(row);
    }

    private void DrawOutdoorReport(OutdoorSnapshot row)
    {
        var id = $"{row.Pocket}{row.UpdatedAt:o}";
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(VenueView.Age(row.UpdatedAt));
        var coords = NearbyScan.PocketCoords(row.Pocket);
        if (coords.Length > 0)
        {
            ImGui.SameLine(0, 0);
            ImGui.TextDisabled(" - ");
            ImGui.SameLine(0, 0);
            ImGui.TextDisabled(coords);
        }
        ImGui.SameLine(0, 4);
        if (ImGui.SmallButton($"Flg##{id}"))
            Here.FlagPocket(row.Pocket);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Map flag on this pocket. Coarse cell, not a person's feet.");
        ImGui.SameLine();
        ImGui.TextColored(UiTheme.TierColor(row.Tier), NearbyScan.TierLabel(row.Tier));
        ImGui.SameLine();
        ImGui.Text($"{Zone.CountLabel(row.Patrons)}/{Zone.CountLabel(row.ZoneCount)}");
        if (string.Equals(row.Pocket, plugin.Session.PocketKey, StringComparison.Ordinal))
        {
            ImGui.SameLine();
            ImGui.TextColored(UiTheme.Amber, "here");
        }
        var info = OutdoorInfo(row);
        if (info.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("[i]");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(info);
        }
    }

    private static string OutdoorTitle(OutdoorZone zone)
    {
        if (zone.Reports.Count > 0
            && NearbyScan.TryParsePocket(zone.Reports[0].Pocket, out _, out var territory, out _, out _))
        {
            var (region, place, _) = Zone.Describe(territory);
            if (place.Length > 0)
                return Zone.Line(zone.World, region, place);
        }
        return Zone.Line(zone.World, zone.Region, zone.Place);
    }

    private static string OutdoorInfo(OutdoorSnapshot row)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Activity))
            bits.Add(row.Activity);
        if (row.InCharacter)
            bits.Add("IC");
        if (row.Glance)
            bits.Add("glances");
        if (row.Emotes)
            bits.Add("emotes");
        if (row.Voices)
            bits.Add("voices");
        if (row.Score > 0)
            bits.Add($"signals +{row.Score}");
        return string.Join(" · ", bits);
    }

    private List<PartakePin> PartakePinsForList()
    {
        var pins = new List<PartakePin>();
        foreach (var pin in plugin.PartakePins)
        {
            if (string.IsNullOrWhiteSpace(pin.Zone))
                continue;
            if (Absorbed(pin))
                continue;
            pins.Add(pin);
        }
        return pins;
    }

    private bool Absorbed(PartakePin pin)
    {
        foreach (var row in plugin.Outdoors)
        {
            if (!SameZone(row, pin))
                continue;
            if (MapNear(row, pin))
                return true;
        }
        return false;
    }

    private PartakePin? NearbyPartake(OutdoorZone zone)
    {
        foreach (var pin in plugin.PartakePins)
        {
            if (zone.Reports.Any(row => SameZone(row, pin) && MapNear(row, pin)))
                return pin;
        }
        return null;
    }

    private static bool SameZone(OutdoorSnapshot row, PartakePin pin)
    {
        if (!string.Equals(row.World, pin.World, StringComparison.OrdinalIgnoreCase))
            return false;
        var place = string.IsNullOrWhiteSpace(row.Zone) ? row.Place : row.Zone;
        return place.Contains(pin.Zone, StringComparison.OrdinalIgnoreCase)
               || pin.Zone.Contains(place, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MapNear(OutdoorSnapshot row, PartakePin pin)
    {
        var coords = NearbyScan.PocketCoords(row.Pocket);
        if (!TryCoords(coords, out var x, out var y))
            return false;
        return Math.Max(Math.Abs(x - pin.X), Math.Abs(y - pin.Y)) <= 1f;
    }

    private bool ZoneBusy(PartakePin pin)
    {
        foreach (var row in plugin.Outdoors)
        {
            if (SameZone(row, pin) && row.ZoneCount >= 55)
                return true;
        }
        return false;
    }

    private static bool TryCoords(string text, out float x, out float y)
    {
        x = 0;
        y = 0;
        var trimmed = (text ?? "").Trim().Trim('(', ')');
        var parts = trimmed.Split(',');
        if (parts.Length != 2)
            return false;
        return float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y);
    }

    private void DrawPartakePins(List<PartakePin> pins)
    {
        ImGui.Separator();
        ImGui.TextDisabled("Partake");
        ImGui.BeginChild("oz-partake", new Vector2(0, 0), true);
        foreach (var pin in pins)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(pin.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"{pin.World} {pin.Zone} ({pin.X:0.0}, {pin.Y:0.0})");
            ImGui.SameLine();
            var territory = Zone.FindTerritory(pin.Zone);
            if (territory == 0)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton($"Flg##p{pin.Id}"))
                Here.FlagMap(territory, pin.X, pin.Y);
            if (territory == 0)
                ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton($"ListingP##p{pin.Id}"))
                OpenPartake(pin.Url);
            if (ZoneBusy(pin))
            {
                ImGui.SameLine();
                ImGui.TextColored(UiTheme.TierColor("busy"), "Zone busy");
            }
        }
        ImGui.EndChild();
    }

    private static void OpenPartake(string url)
    {
        try { Dalamud.Utility.Util.OpenLink(url); }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Open link"); }
    }

    private void DrawOutdoorScanBar()
    {
        var onPlot = plugin.Session.PlotKey.Length > 0;
        var watching = plugin.Session.Watching;
        var ready = plugin.Session.WatchReady;
        var canReport = plugin.Configuration.NoteOutdoorScenes && plugin.CanSend;
        var here = NearbyScan.RunOutdoor(plugin);
        var auditWait = string.IsNullOrEmpty(here.Pocket)
            ? TimeSpan.Zero
            : plugin.Session.OutdoorAuditWait(here.Pocket);

        if (watching)
        {
            if (ImGui.SmallButton("Cancel"))
                plugin.CancelOutdoorWatch();
        }
        else
        {
            var blocked = onPlot || auditWait > TimeSpan.Zero;
            if (blocked)
                ImGui.BeginDisabled();
            var auditLabel = auditWait > TimeSpan.Zero
                ? $"Audit ({Math.Max(1, (int)Math.Ceiling(auditWait.TotalSeconds))}s)"
                : "Audit";
            if (ImGui.SmallButton(auditLabel))
                plugin.StartOutdoorWatch();
            if (blocked)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(onPlot
                    ? "Outdoor reports are for the street, not plots."
                    : auditWait > TimeSpan.Zero
                        ? "Same area waits about 5 minutes. A new zone waits 20 seconds."
                        : "Stay near the start point. About a minute; busier scenes finish sooner.");
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
            ImGui.TextDisabled("Settings → Report Outdoor Scenes to contribute. You can still read the list.");

        if (watching)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextWrapped(plugin.Session.WatchLine);
            if (ready)
            {
                var peak = plugin.Session.WatchPeak;
                var wait = plugin.Session.OutdoorWait(peak.Pocket, peak.Tier);
                var sceneIdx = plugin.Session.OutdoorSceneIdx;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110);
                if (UiTheme.SearchCombo("##oscene", ref sceneIdx, Copy.OutdoorScenes, ref outdoorSceneFilter))
                    plugin.Session.OutdoorSceneIdx = sceneIdx;
                ImGui.SameLine();
                var reportOk = canReport && peak.Tier.Length > 0 && wait <= TimeSpan.Zero;
                if (!reportOk)
                    ImGui.BeginDisabled();
                var label = wait > TimeSpan.Zero
                    ? $"Report ({Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))}s)"
                    : "Report";
                if (ImGui.SmallButton(label))
                    _ = plugin.FinishOutdoorWatch();
                if (!reportOk)
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
            .Where(x => showOther || dcPick.Length == 0 || string.Equals(x.Dc, dcPick, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.World)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => showOther || Reach.CanVisitWorld(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!showOther && worldPick.Length > 0 && !worlds.Contains(worldPick, StringComparer.OrdinalIgnoreCase))
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
        if (ImGui.Checkbox("All Regions", ref showOther))
        {
            plugin.Configuration.ShowOtherRegions = showOther;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Every data center and world in the lists, including ones you cannot visit.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
            ClearFilters(venues);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clear search, data center, world, and status.");
    }

    private void ClearFilters(bool venues)
    {
        query = "";
        dcPick = "";
        worldPick = "";
        dcFilter = "";
        worldFilter = "";
        if (venues)
            venueFilter = 0;
        else
            outdoorFilter = 0;
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
        if (outdoorFilter == 2 && row.Tier != "busy")
            return false;
        if (outdoorFilter == 3 && row.Tier != "some_activity")
            return false;
        if (outdoorFilter == 4 && row.Tier != "light_activity")
            return false;
        if (outdoorFilter == 5 && row.Tier != "some_wandering")
            return false;
        if (string.IsNullOrEmpty(query))
            return true;
        return row.Place.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.World.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (row.Zone ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (row.Activity ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string ZoneKey(OutdoorSnapshot row)
    {
        if (NearbyScan.TryParsePocket(row.Pocket, out var world, out var territory, out _, out _))
            return $"{world}|{territory}";
        var zone = string.IsNullOrWhiteSpace(row.Zone) ? row.Place : row.Zone;
        return $"{row.World}|{zone}";
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

    private sealed record OutdoorZone(
        string Key,
        string World,
        string Region,
        string Place,
        int Rank,
        string Tier,
        int Count,
        DateTimeOffset UpdatedAt,
        List<OutdoorSnapshot> Reports);
}
