using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LightsOn.Api;
using LightsOn.Scan;

namespace LightsOn.Windows;

internal static class VenueView
{
    public static void Draw(Plugin plugin, VenueListing venue, bool currentPlot)
    {
        var loc = venue.Location;
        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        var onPlot = NearbyScan.MatchesVenue(venue);
        var here = HousingReader.Read();

        ImGui.TextWrapped(venue.Name ?? "");
        if (!currentPlot)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Pop out"))
                plugin.OpenPlotWindow();
        }

        var lean = Lean(occ);
        ImGui.TextColored(BadgeColor(occ), SummaryStatus(occ, venue.Log, lean));
        ImGui.SameLine();
        ImGui.TextDisabled(venue.HoursLine);

        if (onPlot)
            ImGui.TextWrapped($"On this plot · {(here.Inside ? "inside" : "outside")} · {loc?.Address ?? here.Summary}");
        else
        {
            ImGui.TextWrapped(loc?.Address ?? "");
            if (loc is not null && Lifestream.Installed() && Reach.CanVisitWorld(loc.World))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Travel to"))
                {
                    try { Lifestream.Go(loc); }
                    catch (Exception ex) { Plugin.Log.Verbose(ex, "Lifestream"); }
                }
            }
        }

        ImGui.TextDisabled(FlagsLine(venue));
        var story = venue.DescriptionText;
        if (story.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(story.Length > 600 ? story[..600] + "…" : story);
        }

        ImGui.Spacing();
        DrawCheck(plugin, venue, onPlot, here);

        DrawReportLog(plugin, venue);
        DrawLogBook(plugin, venue, onPlot);
    }

    private static void DrawCheck(Plugin plugin, VenueListing venue, bool onPlot, HousingAddress here)
    {
        if (!NearbyScan.OccupancyEligible(venue))
        {
            ImGui.TextDisabled(Copy.NoPlot);
            return;
        }

        if (!onPlot)
        {
            ImGui.TextDisabled("Travel to the plot to scan or send.");
            return;
        }

        var scanWait = plugin.Session.ScanWait;
        if (scanWait > TimeSpan.Zero)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton(scanWait > TimeSpan.Zero
                ? $"Scan ({(int)Math.Ceiling(scanWait.TotalSeconds)}s)"
                : "Scan"))
        {
            var snap = plugin.ScanNow();
            plugin.Session.SetAction(venue.Id, snap.Summary);
        }
        if (scanWait > TimeSpan.Zero)
            ImGui.EndDisabled();

        ImGui.SameLine();
        var canSend = plugin.CanSend;
        DrawSend(plugin, venue, here.Inside ? Copy.HappeningButton : Copy.YardBusy, "happening", canSend, here.Inside);
        ImGui.SameLine();
        DrawSend(plugin, venue, here.Inside ? Copy.WrappedButton : Copy.YardQuiet, "wrapped_up", canSend, here.Inside);
        if (!here.Inside && HousingReader.DoorIsLocked())
        {
            ImGui.SameLine();
            DrawSend(plugin, venue, Copy.DoorLocked, "door_locked", canSend, false);
        }

        var action = plugin.Session.ActionFor(venue.Id);
        if (action.Length > 0)
            ImGui.TextWrapped(action);
        else
            ImGui.TextWrapped(plugin.LastScanLine);

        if (plugin.Session.ObserveSince != default)
        {
            var observe = DateTimeOffset.UtcNow - plugin.Session.ObserveSince;
            if (observe < TimeSpan.FromSeconds(Limits.ObserveSeconds))
                ImGui.TextDisabled($"watching {Limits.ObserveSeconds - (int)observe.TotalSeconds}s for extra signals");
        }

        if (!canSend)
            ImGui.TextDisabled(plugin.Configuration.ListingsOnly
                ? "Listings only is on."
                : plugin.Configuration.ReporterResetLockRemaining > TimeSpan.Zero
                    ? "Reports locked after reporter id reset."
                    : "Settings → Send reports.");
        else
            ImGui.TextDisabled("Scan is local. Quiet is always a button. Lanterns may send themselves.");
    }

    private static void DrawSend(Plugin plugin, VenueListing venue, string label, string kind, bool canSend, bool inside)
    {
        var action = kind == "door_locked"
            ? "door"
            : kind == "happening"
                ? (inside ? "happening-in" : "happening-yard")
                : (inside ? "wrapped-in" : "wrapped-yard");
        var wait = plugin.Session.SendWait(venue.Id, action);
        var blocked = !canSend || wait > TimeSpan.Zero;
        if (blocked)
            ImGui.BeginDisabled();
        var text = wait > TimeSpan.Zero ? $"{label} ({(int)Math.Ceiling(wait.TotalSeconds)}s)" : label;
        if (ImGui.SmallButton($"{text}##{action}"))
            _ = plugin.ReportUi(venue, kind);
        if (blocked)
            ImGui.EndDisabled();
    }

    private static string? logFor;

    private static void DrawReportLog(Plugin plugin, VenueListing venue)
    {
        if (logFor != venue.Id)
        {
            logFor = venue.Id;
            _ = plugin.RefreshLog(venue);
            _ = plugin.RefreshNotes(venue);
        }

        var log = venue.Log;
        var exterior = log.Where(e => !e.Inside).Take(Limits.ReportCap).ToList();
        var interior = log.Where(e => e.Inside).Take(Limits.ReportCap).ToList();
        var locked = log.Any(e => e.DoorLocked) || venue.Occupancy.DoorLocked;

        ImGui.Separator();
        ImGui.TextColored(UiTheme.Teal, $"Exterior  {Score(exterior)}");
        if (exterior.Count == 0)
            ImGui.TextDisabled("None yet.");
        foreach (var row in exterior)
            ImGui.TextWrapped(EventLine(row));

        ImGui.Separator();
        var lockMark = locked && interior.Count > 0 ? "  locked?" : locked ? "  locked" : "";
        ImGui.TextColored(UiTheme.Teal, $"Interior{lockMark}  {Score(interior)}");
        if (interior.Count == 0)
            ImGui.TextDisabled(locked ? "locked. No interior reports yet." : "None yet.");
        foreach (var row in interior)
            ImGui.TextWrapped(EventLine(row));
    }

    private static int adjIdx;
    private static int nounIdx;

    private static void DrawLogBook(Plugin plugin, VenueListing venue, bool onPlot)
    {
        ImGui.Separator();
        UiTheme.Section("Log book", true);
        ImGui.TextWrapped(Copy.LogBookHint);
        foreach (var note in venue.Notes.Take(12))
            ImGui.TextWrapped($"{Age(note.At)}: \"{note.Text}\"");
        if (venue.Notes.Count == 0)
            ImGui.TextDisabled("No notes yet.");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.32f);
        ImGui.Combo("##adj", ref adjIdx, Copy.LogAdjectives, Copy.LogAdjectives.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.40f);
        ImGui.Combo("##noun", ref nounIdx, Copy.LogNouns, Copy.LogNouns.Length);

        var ready = onPlot && plugin.Configuration.AllowLogBook && plugin.CanSend
                    && venue.Occupancy?.IsHappening == true
                    && plugin.Session.OnPlot >= TimeSpan.FromMinutes(Limits.LogBookDwellMinutes);
        ImGui.SameLine();
        if (!ready)
            ImGui.BeginDisabled();
        var wait = TimeSpan.FromMinutes(Limits.LogBookDwellMinutes) - plugin.Session.OnPlot;
        var label = ready
            ? "+ Note"
            : onPlot && venue.Occupancy?.IsHappening == true && wait > TimeSpan.Zero
                ? $"+ Note ({FmtWait(wait)})"
                : "+ Note";
        if (ImGui.SmallButton(label))
            _ = plugin.LeaveNoteUi(venue, Copy.LogLine(adjIdx, nounIdx));
        if (!ready)
            ImGui.EndDisabled();
        if (!onPlot)
            ImGui.TextDisabled("Go to the plot to leave a note.");
        else if (venue.Occupancy?.IsHappening != true)
            ImGui.TextDisabled("Log book opens when lanterns are lit.");
    }

    private static string FmtWait(TimeSpan wait)
    {
        if (wait.TotalMinutes >= 1)
            return $"{Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))}m";
        return $"{Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))}s";
    }

    public static Vector4 BadgeColor(OccupancySnapshot occ)
    {
        var (_, color) = OpenBadge(occ);
        return color;
    }

    public static (string Label, Vector4 Color) OpenBadge(OccupancySnapshot occ)
    {
        if (occ.HappeningReports == 0 && occ.WrappedUpReports == 0)
            return ("open?", UiTheme.Yellow);
        var lean = Lean(occ);
        if (lean > 0)
            return ("open!", UiTheme.Happening);
        if (lean == 0)
            return ("open~", UiTheme.Amber);
        return ("\"open\"", UiTheme.Orange);
    }

    public static int Lean(OccupancySnapshot occ) =>
        occ.InteriorHappening * 2 + occ.ExteriorHappening
        - occ.InteriorWrapped * 2 - occ.ExteriorWrapped;

    private static string SummaryStatus(OccupancySnapshot occ, System.Collections.Generic.IReadOnlyList<OccupancyEvent> log, int lean)
    {
        var head = lean > 0 ? Copy.Happening : lean < 0 ? Copy.Wrapped : occ.HappeningReports + occ.WrappedUpReports == 0 ? Copy.NoReport : "Split";
        var bits = new System.Collections.Generic.List<string> { head };
        var locked = occ.DoorLocked || log.Any(e => e.DoorLocked);
        var hasIn = log.Any(e => e.Inside) || occ.InteriorHappening + occ.InteriorWrapped > 0;
        if (locked && hasIn)
            bits.Add("locked?");
        else if (locked)
            bits.Add("locked");
        else if (lean > 0 || occ.HappeningReports + occ.WrappedUpReports > 0)
            bits.Add("Unlocked");
        var inside = occ.InteriorHappening + occ.InteriorWrapped;
        var outside = occ.ExteriorHappening + occ.ExteriorWrapped;
        if (inside > outside)
            bits.Add("Mostly inside");
        else if (outside > inside)
            bits.Add("Mostly yard");
        return string.Join(" · ", bits);
    }

    private static string FlagsLine(VenueListing venue)
    {
        var bits = new System.Collections.Generic.List<string> { venue.Sfw ? "SFW" : "NSFW" };
        if (venue.Hiring)
            bits.Add("Hiring");
        if (venue.Tags is { Count: > 0 })
            bits.AddRange(venue.Tags.Take(4));
        if (!string.IsNullOrWhiteSpace(venue.Website))
            bits.Add("Website");
        if (!string.IsNullOrWhiteSpace(venue.Discord))
            bits.Add("Discord");
        return string.Join(" · ", bits);
    }

    private static string EventLine(OccupancyEvent row)
    {
        var what = row.Inside
            ? (row.Kind == "happening" ? Copy.Happening : Copy.Wrapped)
            : (row.Kind == "happening" ? Copy.YardBusy : Copy.YardQuiet);
        var bits = new System.Collections.Generic.List<string> { what };
        if (row.DoorLocked)
            bits.Add("locked");
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
        return $"+{up} / -{down}";
    }

    public static string Age(DateTimeOffset? at)
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
