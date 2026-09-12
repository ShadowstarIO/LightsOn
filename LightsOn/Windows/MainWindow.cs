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
    private int filter;
    private string? selectedId;
    private string noteDraft = "";
    private string? notesFor;
    private static readonly string[] Filters = ["All", "Marked open", "Lanterns lit"];

    public MainWindow(Plugin plugin)
        : base("LightsOn###LightsOnMain")
    {
        this.plugin = plugin;
        Size = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(580, 380),
            MaximumSize = new Vector2(1100, 920),
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

        var hours = venue.Resolution?.IsNow == true ? Copy.MarkedOpen : "listed, not in posted hours";
        ImGui.TextWrapped($"{venue.Name ?? ""} — {hours}");
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

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##q", "Search name or world", ref query, 80);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.Combo("##filter", ref filter, Filters, Filters.Length);

        var rows = plugin.Venues.Where(Matches).ToList();
        var selected = rows.FirstOrDefault(v => v.Id == selectedId) ?? rows.FirstOrDefault();
        selectedId = selected?.Id;

        var listW = Math.Max(280, ImGui.GetContentRegionAvail().X * 0.48f);
        ImGui.BeginChild("list", new Vector2(listW, -1), true);
        foreach (var venue in rows)
        {
            var loc = venue.Location;
            var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
            var mark = occ.IsHappening ? "● " : occ.IsWrappedUp ? "○ " : "  ";
            var label = $"{mark}{venue.Name ?? ""}##{venue.Id}";
            if (ImGui.Selectable(label, venue.Id == selectedId))
                selectedId = venue.Id;
            if (occ.IsHappening)
                ImGui.TextColored(UiTheme.Happening, HappeningLabel(occ));
            else if (occ.IsWrappedUp)
                ImGui.TextColored(UiTheme.Wrapped, WrappedLabel(occ));
            else if (venue.Resolution?.IsNow == true)
                ImGui.TextDisabled(Copy.MarkedOpen);
            if (loc is not null)
                ImGui.TextWrapped(loc.Address);
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
        if (loc is not null)
            ImGui.TextWrapped(loc.Address);
        ImGui.TextDisabled(venue.Sfw ? "SFW listing" : "NSFW listing");

        ImGui.Spacing();
        if (occ.IsHappening)
        {
            ImGui.TextColored(UiTheme.Happening, Copy.Happening);
            ImGui.TextWrapped(HappeningLabel(occ));
        }
        else if (occ.IsWrappedUp)
        {
            ImGui.TextColored(UiTheme.Wrapped, Copy.Wrapped);
            ImGui.TextWrapped(WrappedLabel(occ));
        }
        else if (venue.Resolution?.IsNow == true)
            ImGui.TextWrapped($"{Copy.MarkedOpen} — {Copy.NoReport}");
        else
            ImGui.TextDisabled(Copy.NoReport);

        ImGui.Spacing();
        UiTheme.Section("On this plot", true);
        DrawCheck(venue, false);

        if (plugin.ActionLine.Length > 0)
            ImGui.TextWrapped(plugin.ActionLine);

        DrawReportLog(venue);

        if (occ.IsHappening)
            DrawLogBook(venue, loc is not null && NearbyScan.MatchesVenue(venue));
    }

    private void DrawReportLog(VenueListing venue)
    {
        if (notesFor != venue.Id)
        {
            notesFor = venue.Id;
            _ = plugin.RefreshLog(venue);
            _ = plugin.RefreshNotes(venue);
        }

        ImGui.Separator();
        UiTheme.Section("Reports", true);
        var log = venue.Log;
        if (log.Count == 0)
        {
            ImGui.TextDisabled("No occupancy reports in the last 20 minutes.");
            return;
        }

        var litInside = log.Any(e => e.Kind == "happening" && e.Inside);
        var quietYard = log.Any(e => e.Kind == "wrapped_up");
        if (litInside && quietYard)
            ImGui.TextWrapped(Copy.MixedReports);

        foreach (var row in log)
        {
            var what = row.Kind == "happening" ? Copy.Happening : Copy.Wrapped;
            var layer = row.Inside ? "from inside" : "from the yard";
            ImGui.TextWrapped($"{Age(row.At)} — {what} · {layer}");
        }
    }

    private void DrawCheck(VenueListing venue, bool compact)
    {
        if (!NearbyScan.OccupancyEligible(venue))
        {
            ImGui.TextDisabled(Copy.ApartmentSkip);
            return;
        }

        var onPlot = NearbyScan.MatchesVenue(venue);
        if (!onPlot)
        {
            ImGui.TextDisabled("Travel to the plot to check occupancy.");
            return;
        }

        var check = plugin.Session.Check;
        ImGui.TextWrapped(check.Guide);
        ImGui.TextDisabled(plugin.LastScanLine);
        if (check.HasYard)
            ImGui.TextDisabled(check.Yard!.Value.ThresholdMet ? "Yard · enough company" : "Yard · quiet");
        if (check.HasInside)
            ImGui.TextDisabled(check.Inside!.Value.ThresholdMet ? "Inside · enough company" : "Inside · quiet");
        if (check.DoorLocked)
            ImGui.TextDisabled("Door locked — interior skipped.");

        var canSend = plugin.CanSend;
        if (onPlot && !check.HasInside && check.HasYard && !check.DoorLocked)
        {
            if (ImGui.SmallButton(Copy.DoorLocked))
                plugin.MarkDoorLocked();
            if (!compact)
                ImGui.SameLine();
        }

        if (!canSend || !check.Ready)
            ImGui.BeginDisabled();
        if (check.Enough)
        {
            if (ImGui.Button(Copy.HappeningButton))
                _ = Report(venue, "happening");
        }
        else
        {
            if (ImGui.Button(Copy.WrappedButton))
                _ = Report(venue, "wrapped_up");
        }
        if (!canSend || !check.Ready)
            ImGui.EndDisabled();

        if (!plugin.CanSend)
            ImGui.TextDisabled(plugin.Configuration.ListingsOnly
                ? "Listings only is on."
                : plugin.Configuration.ReporterResetLockRemaining > TimeSpan.Zero
                    ? "Reports locked after reporter id reset."
                    : "Settings → Send reports.");
        else if (!check.Ready)
            ImGui.TextDisabled("Nothing is sent until both layers are checked (or the door is locked).");
    }

    private void DrawLogBook(VenueListing venue, bool onPlot)
    {
        ImGui.Spacing();
        UiTheme.Section("Log book", true);
        ImGui.TextDisabled(Copy.LogBookHint);
        if (venue.Notes.Count == 0)
            ImGui.TextDisabled("No notes tonight.");
        foreach (var note in venue.Notes)
        {
            ImGui.BulletText(note.Text);
            ImGui.SameLine();
            ImGui.TextDisabled(Age(note.At));
        }

        var ready = onPlot && plugin.Configuration.AllowLogBook && plugin.CanSend
                    && plugin.Session.OnPlot >= TimeSpan.FromMinutes(20);
        if (!ready)
        {
            ImGui.TextDisabled("Stay about 20 minutes with the lanterns lit to leave a note.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##note", "great music, kind host…", ref noteDraft, 80);
        if (ImGui.Button("Leave note") && noteDraft.Trim().Length > 0)
        {
            var text = noteDraft;
            noteDraft = "";
            _ = LeaveNote(venue, text);
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
            ImGui.TextUnformatted(row.Place);
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
        if (filter == 1 && venue.Resolution?.IsNow != true)
            return false;
        if (filter == 2 && venue.Occupancy?.IsHappening != true)
            return false;
        if (string.IsNullOrEmpty(query))
            return true;
        var loc = venue.Location;
        return (venue.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.World ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.DataCenter ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.District ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int TierRank(string tier) => tier switch
    {
        "extremely_busy" => 0,
        "some_activity" => 1,
        "some_wandering" => 2,
        _ => 3,
    };

    private static string HappeningLabel(OccupancySnapshot occ)
        => $"{occ.HappeningReports} report{(occ.HappeningReports == 1 ? "" : "s")} · {Age(occ.UpdatedAt)}";

    private static string WrappedLabel(OccupancySnapshot occ)
        => $"{occ.WrappedUpReports} report{(occ.WrappedUpReports == 1 ? "" : "s")} · {Age(occ.UpdatedAt)}";

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
