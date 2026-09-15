using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LightsOn.Api;
using LightsOn.Scan;

namespace LightsOn.Windows;

internal static class VenueView
{
    private static string adjFilter = "";
    private static string nounFilter = "";
    private static int adjIdx;
    private static int nounIdx;
    private static string? logFor;

    public static void Draw(Plugin plugin, VenueListing venue, bool currentPlot)
    {
        var loc = venue.Location;
        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        var onPlot = NearbyScan.MatchesVenue(venue);
        var here = HousingReader.Read();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(UiTheme.Title, venue.Name ?? "");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
            _ = plugin.RefreshVenue(venue);
        ImGui.SameLine();
        if (currentPlot)
        {
            if (ImGui.SmallButton("Full"))
                plugin.ShowFull(venue.Id);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open the full LightsOn window for this listing.");
        }
        else
        {
            if (ImGui.SmallButton("Mini"))
                plugin.ShowMini();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open the small Current Plot window.");
        }

        var lean = Lean(occ);
        ImGui.TextColored(BadgeColor(occ), SummaryStatus(occ, venue.Log, lean));
        ImGui.SameLine();
        ImGui.TextDisabled(venue.HoursLine);

        if (onPlot)
            ImGui.TextWrapped($"On This Plot · {(here.Inside ? "inside" : "yard")} · {loc?.Address ?? here.Summary}");
        else
        {
            ImGui.TextWrapped(loc?.Address ?? "");
            if (loc is not null && Lifestream.Installed() && Reach.CanVisitWorld(loc.World))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Travel"))
                {
                    try { Lifestream.Go(loc); }
                    catch (Exception ex) { Plugin.Log.Verbose(ex, "Lifestream"); }
                }
            }
        }

        DrawLinks(venue);
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

    private static void DrawLinks(VenueListing venue)
    {
        ImGui.TextDisabled(FlagsLine(venue));
        if (ImGui.SmallButton("Listing"))
            OpenUrl(Copy.DirectoryUrl);
        if (!string.IsNullOrWhiteSpace(venue.Website))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Website"))
                OpenUrl(venue.Website);
        }
        if (!string.IsNullOrWhiteSpace(venue.Discord))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Discord"))
                OpenUrl(venue.Discord);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Dalamud.Utility.Util.OpenLink(url); }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Open link"); }
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
            ImGui.TextDisabled("Go to this plot to scan.");
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
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Local scan. Quiet is always a button. Lanterns may send themselves.");

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
                ImGui.TextDisabled($"Watching {Limits.ObserveSeconds - (int)observe.TotalSeconds}s");
        }
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

        UiTheme.Gap();
        ImGui.Separator();
        ImGui.TextColored(UiTheme.Teal, $"Exterior  {Score(exterior)}");
        if (exterior.Count == 0)
            ImGui.TextDisabled("None yet.");
        foreach (var row in exterior)
            ImGui.TextColored(UiTheme.AgeColor(row.At), EventLine(row));

        UiTheme.Gap();
        ImGui.Separator();
        var lockMark = locked && interior.Count > 0 ? "  locked?" : locked ? "  locked" : "";
        ImGui.TextColored(UiTheme.Teal, $"Interior{lockMark}  {Score(interior)}");
        if (interior.Count == 0)
            ImGui.TextDisabled(locked ? "Locked. No interior reports yet." : "None yet.");
        foreach (var row in interior)
            ImGui.TextColored(UiTheme.AgeColor(row.At), EventLine(row));
    }

    private static void DrawLogBook(Plugin plugin, VenueListing venue, bool onPlot)
    {
        UiTheme.Gap();
        ImGui.Separator();
        UiTheme.Section("Log Book", true);

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.32f);
        UiTheme.SearchCombo("##adj", ref adjIdx, Copy.LogAdjectives, ref adjFilter);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.40f);
        UiTheme.SearchCombo("##noun", ref nounIdx, Copy.LogNouns, ref nounFilter);

        var wait = TimeSpan.FromMinutes(Limits.LogBookDwellMinutes) - plugin.Session.OnPlot;
        var ready = onPlot && plugin.Configuration.AllowLogBook && plugin.CanSend
                    && venue.Occupancy?.IsHappening == true
                    && wait <= TimeSpan.Zero;
        ImGui.SameLine();
        if (!ready)
            ImGui.BeginDisabled();
        var label = ready
            ? "+ Note"
            : onPlot && venue.Occupancy?.IsHappening == true && wait > TimeSpan.Zero
                ? $"+ Note ({FmtWait(wait)})"
                : "+ Note";
        if (ImGui.SmallButton(label))
            _ = plugin.LeaveNoteUi(venue, Copy.LogLine(adjIdx, nounIdx));
        if (!ready)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!onPlot)
                ImGui.SetTooltip("Go to the plot to leave a note.");
            else if (venue.Occupancy?.IsHappening != true)
                ImGui.SetTooltip("Log book opens when lanterns are lit.");
            else if (wait > TimeSpan.Zero)
                ImGui.SetTooltip($"Stay about {Limits.LogBookDwellMinutes} minutes on the property.");
            else
                ImGui.SetTooltip("Leave this pair in the log book.");
        }

        if (venue.Notes.Count == 0)
            ImGui.TextDisabled("No notes yet.");
        foreach (var note in venue.Notes.Take(12))
            ImGui.TextColored(UiTheme.AgeColor(note.At), $"{Age(note.At)}: \"{note.Text}\"");
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
        if (Math.Abs(lean) < 0.05f)
            return ("open~", UiTheme.Amber);
        return ("\"open\"", UiTheme.Orange);
    }

    public static float Lean(OccupancySnapshot occ) => occ.LeanValue;

    private static string SummaryStatus(OccupancySnapshot occ, IReadOnlyList<OccupancyEvent> log, float lean)
    {
        var head = lean > 0 ? Copy.Happening : lean < 0 ? Copy.Wrapped : occ.HappeningReports + occ.WrappedUpReports == 0 ? Copy.NoReport : "Split";
        var bits = new List<string> { head };
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
        var bits = new List<string> { venue.Sfw ? "SFW" : "NSFW" };
        if (venue.Hiring)
            bits.Add("Hiring");
        if (venue.Tags is { Count: > 0 })
            bits.AddRange(venue.Tags.Take(4));
        return string.Join(" · ", bits);
    }

    private static string EventLine(OccupancyEvent row)
    {
        var what = row.Inside
            ? (row.Kind == "happening" ? Copy.Happening : Copy.Wrapped)
            : (row.Kind == "happening" ? Copy.YardBusy : Copy.YardQuiet);
        var bits = new List<string> { what };
        if (row.DoorLocked)
            bits.Add("locked");
        if (row.ThresholdMet)
            bits.Add("patrons");
        if (row.Voices)
            bits.Add("voices");
        if (row.Glance)
            bits.Add("glances");
        if (row.Music)
            bits.Add("music");
        if (!row.ThresholdMet && row.Kind != "happening")
            bits.Add("quiet");
        return $"{Age(row.At)}  {string.Join(" · ", bits)}";
    }

    private static string Score(List<OccupancyEvent> rows)
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
        var hours = mins / 60.0;
        if (hours < 2)
            return "1h ago";
        return $"{(int)hours}h ago";
    }
}
