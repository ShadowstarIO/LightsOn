using System;
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
    private int worldIdx;
    private int filter;
    private string? selectedId;
    private int noteIdx;
    private string? notesFor;
    private string[] worlds = ["All worlds"];
    private static readonly string[] StatusFilters = ["All", "Lanterns lit", "Open now", "Vacant", "No data"];

    public MainWindow(Plugin plugin)
        : base("LightsOn###LightsOnMain")
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

        DrawHop();
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

    private void DrawHop()
    {
        var venue = plugin.Session.Hop;
        if (venue is null || plugin.Session.HopDismissed || !plugin.Configuration.PromptOnEnter)
            return;
        if (!NearbyScan.MatchesVenue(venue))
            return;

        ImGui.TextWrapped($"{venue.Name ?? ""} — {venue.HoursLine}");
        DrawCheck(venue, true);
        if (ImGui.SmallButton("Not now"))
            plugin.Session.HopDismissed = true;
        ImGui.Separator();
    }

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

        worlds = new[] { "All worlds" }
            .Concat(plugin.Venues.Select(v => v.Location?.World ?? "")
                .Where(w => w.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(w => w, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (worldIdx >= worlds.Length)
            worldIdx = 0;

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##q", "Search name", ref query, 80);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("##world", ref worldIdx, worlds, worlds.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140);
        ImGui.Combo("##status", ref filter, StatusFilters, StatusFilters.Length);

        var rows = plugin.Venues
            .Where(Matches)
            .OrderByDescending(v => v.Resolution?.IsNow == true)
            .ThenBy(v => v.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = rows.FirstOrDefault(v => v.Id == selectedId) ?? rows.FirstOrDefault();
        selectedId = selected?.Id;

        var listW = Math.Max(280, ImGui.GetContentRegionAvail().X * 0.46f);
        ImGui.BeginChild("list", new Vector2(listW, -1), true);
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
                ImGui.TextColored(UiTheme.Happening, "open");
            }
            if (loc is not null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(loc.Address);
            }
        }
        if (rows.Count == 0)
            ImGui.TextDisabled("No venues match.");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("detail", new Vector2(0, -1), true);
        if (selected is null)
            ImGui.TextDisabled("Pick a venue.");
        else
            DrawDetail(selected);
        ImGui.EndChild();
    }

    private void DrawDetail(VenueListing venue)
    {
        var loc = venue.Location;
        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        ImGui.TextWrapped(venue.Name ?? "");
        ImGui.TextWrapped(venue.HoursLine);
        if (loc is not null)
        {
            ImGui.TextWrapped(loc.Address);
            if (Lifestream.Installed())
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Travel to"))
                {
                    try { Lifestream.Go(loc); }
                    catch (Exception ex) { plugin.ActionLine = "Travel failed."; Plugin.Log.Verbose(ex, "Lifestream"); }
                }
            }
        }

        ImGui.TextDisabled(FlagsLine(venue));

        ImGui.Spacing();
        if (occ.IsMixed)
        {
            ImGui.TextColored(UiTheme.Amber, "Mixed reports");
            ImGui.TextWrapped(Copy.MixedReports);
        }
        else if (occ.IsHappening)
        {
            ImGui.TextColored(UiTheme.Happening, Copy.Happening);
            ImGui.TextWrapped(SummaryLine(occ, venue.Log));
        }
        else if (occ.IsWrappedUp)
        {
            ImGui.TextColored(UiTheme.Wrapped, Copy.Wrapped);
            ImGui.TextWrapped(SummaryLine(occ, venue.Log));
        }
        else
            ImGui.TextDisabled(Copy.NoReport);

        if (occ.BothLayers)
            ImGui.TextDisabled("Yard and room both reported.");
        else if (occ.HappeningReports + occ.WrappedUpReports > 0)
            ImGui.TextDisabled("One layer only — lighter weight until the other is reported.");

        ImGui.Spacing();
        UiTheme.Section("On this plot", true);
        DrawCheck(venue, false);
        if (plugin.ActionLine.Length > 0)
            ImGui.TextWrapped(plugin.ActionLine);

        DrawReportLog(venue);
        DrawLogBook(venue, loc is not null && NearbyScan.MatchesVenue(venue));
    }

    private void DrawCheck(VenueListing venue, bool compact)
    {
        if (!NearbyScan.OccupancyEligible(venue))
        {
            ImGui.TextDisabled(Copy.NoPlot);
            return;
        }

        var onPlot = NearbyScan.MatchesVenue(venue);
        if (!onPlot)
        {
            ImGui.TextDisabled("Travel to the plot to report this layer.");
            return;
        }

        var scan = plugin.Session.Check;
        var here = HousingReader.Read();
        ImGui.TextWrapped(here.Inside ? "You are inside." : "You are in the yard.");
        ImGui.TextWrapped(plugin.LastScanLine);

        var canSend = plugin.CanSend;
        if (!canSend)
            ImGui.BeginDisabled();
        if (ImGui.Button(Copy.HappeningButton))
            _ = Report(venue, "happening");
        ImGui.SameLine();
        if (ImGui.Button(Copy.WrappedButton))
            _ = Report(venue, "wrapped_up");
        if (!here.Inside)
        {
            ImGui.SameLine();
            if (ImGui.Button(Copy.DoorLocked))
                _ = Report(venue, "door_locked");
        }
        if (!canSend)
            ImGui.EndDisabled();

        if (!plugin.CanSend)
            ImGui.TextDisabled(plugin.Configuration.ListingsOnly
                ? "Listings only is on."
                : plugin.Configuration.ReporterResetLockRemaining > TimeSpan.Zero
                    ? "Reports locked after reporter id reset."
                    : "Settings → Send reports.");
        else
            ImGui.TextDisabled("Sends this layer only. Yard and room are listed separately.");
        _ = scan;
        _ = compact;
    }

    private void DrawReportLog(VenueListing venue)
    {
        if (notesFor != venue.Id)
        {
            notesFor = venue.Id;
            noteIdx = 0;
            _ = plugin.RefreshLog(venue);
            _ = plugin.RefreshNotes(venue);
        }

        var log = venue.Log;
        var exterior = log.Where(e => !e.Inside).Take(12).ToList();
        var interior = log.Where(e => e.Inside).Take(12).ToList();
        var locked = log.Any(e => e.DoorLocked) || venue.Occupancy.DoorLocked;

        ImGui.Separator();
        var extScore = Score(exterior);
        ImGui.TextColored(UiTheme.Teal, $"Exterior reports  {extScore}");
        if (exterior.Count == 0)
            ImGui.TextDisabled("None yet.");
        foreach (var row in exterior)
            ImGui.TextWrapped(EventLine(row));

        ImGui.Separator();
        var intScore = Score(interior);
        var lockMark = locked ? "  🔒" : "";
        ImGui.TextColored(UiTheme.Teal, $"Interior reports{lockMark}  {intScore}");
        if (locked && interior.Count == 0)
            ImGui.TextDisabled("Door reported locked from the yard.");
        else if (interior.Count == 0)
            ImGui.TextDisabled("None yet.");
        foreach (var row in interior)
            ImGui.TextWrapped(EventLine(row));
    }

    private void DrawLogBook(VenueListing venue, bool onPlot)
    {
        ImGui.Separator();
        UiTheme.Section("Log book", true);
        ImGui.TextWrapped(Copy.LogBookHint);
        if (venue.Notes.Count == 0)
            ImGui.TextDisabled("No notes yet.");
        foreach (var note in venue.Notes.Take(12))
            ImGui.TextWrapped($"· {note.Text}  {Age(note.At)}");

        var ready = onPlot && plugin.Configuration.AllowLogBook && plugin.CanSend
                    && venue.Occupancy?.IsHappening == true
                    && plugin.Session.OnPlot >= TimeSpan.FromMinutes(Limits.LogBookDwellMinutes);
        if (!ready)
        {
            ImGui.TextDisabled("Stay about 20 minutes with the lanterns lit to leave a note.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##phrase", ref noteIdx, Copy.LogPhrases, Copy.LogPhrases.Length);
        if (ImGui.Button("Leave note"))
            _ = LeaveNote(venue, Copy.LogPhrases[Math.Clamp(noteIdx, 0, Copy.LogPhrases.Length - 1)]);
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

    private async System.Threading.Tasks.Task Report(VenueListing venue, string kind)
    {
        plugin.ActionLine = "Scanning…";
        plugin.ActionLine = await plugin.TryReport(venue, kind).ConfigureAwait(true);
    }

    private async System.Threading.Tasks.Task LeaveNote(VenueListing venue, string text)
    {
        plugin.ActionLine = await plugin.TryNote(venue, text).ConfigureAwait(true);
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
        if (worldIdx > 0 && worldIdx < worlds.Length)
        {
            if (!string.Equals(venue.Location?.World, worlds[worldIdx], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (string.IsNullOrEmpty(query))
            return true;
        var loc = venue.Location;
        return (venue.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.World ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.District ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string FlagsLine(VenueListing venue)
    {
        var bits = new System.Collections.Generic.List<string> { venue.Sfw ? "SFW" : "NSFW" };
        if (venue.Hiring)
            bits.Add("Hiring");
        if (!string.IsNullOrWhiteSpace(venue.Website))
            bits.Add("Website");
        if (!string.IsNullOrWhiteSpace(venue.Discord))
            bits.Add("Discord");
        return string.Join(" · ", bits);
    }

    private static string SummaryLine(OccupancySnapshot occ, System.Collections.Generic.IReadOnlyList<OccupancyEvent> log)
    {
        var bits = new System.Collections.Generic.List<string>();
        if (occ.DoorLocked || log.Any(e => e.DoorLocked))
            bits.Add("🔒 door");
        else
            bits.Add("Unlocked");
        if (log.Any(e => e.ThresholdMet))
            bits.Add("Patrons");
        if (log.Any(e => e.Voices))
            bits.Add("Voices nearby");
        if (log.Any(e => e.Glance))
            bits.Add("Glances");
        if (log.Any(e => e.Music))
            bits.Add("Music");
        var inside = occ.InteriorHappening + occ.InteriorWrapped;
        var outside = occ.ExteriorHappening + occ.ExteriorWrapped;
        if (inside > outside)
            bits.Add("Mostly inside");
        else if (outside > inside)
            bits.Add("Mostly yard");
        if (occ.IsWrappedUp && bits.Count <= 1)
            bits.Add("Quiet");
        return string.Join(" · ", bits);
    }

    private static string EventLine(OccupancyEvent row)
    {
        var what = row.Kind == "happening" ? Copy.Happening : Copy.Wrapped;
        var bits = new System.Collections.Generic.List<string> { what };
        if (row.DoorLocked)
            bits.Add("🔒");
        if (row.ThresholdMet)
            bits.Add("patrons");
        if (row.Voices)
            bits.Add("voices");
        if (row.Glance)
            bits.Add("glance");
        if (row.Music)
            bits.Add("music");
        if (!row.ThresholdMet && row.Kind != "happening")
            bits.Add("quiet");
        return $"{Age(row.At)}  {string.Join(" · ", bits)}";
    }

    private static string Score(System.Collections.Generic.List<OccupancyEvent> rows)
    {
        var up = rows.Count(e => e.Kind == "happening");
        var down = rows.Count(e => e.Kind == "wrapped_up");
        if (up == 0 && down == 0)
            return "";
        return $"+{up} / −{down}";
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
