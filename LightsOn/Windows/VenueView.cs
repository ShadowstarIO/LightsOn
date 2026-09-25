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
        var onPlot = NearbyScan.MatchesVenue(venue);
        var here = HousingReader.Read();
        var apartment = loc?.IsApartment == true;
        var inside = onPlot && (here.Inside || here.OnApartment);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(UiTheme.Title, venue.Name ?? "");
        ImGui.SameLine(0, 0);
        ImGui.TextDisabled(" - ");
        ImGui.SameLine(0, 0);
        ImGui.Text(apartment ? "Apartment" : "Plot");
        if (onPlot)
        {
            ImGui.SameLine(0, 0);
            ImGui.TextDisabled(" - ");
            ImGui.SameLine(0, 0);
            ImGui.Text(inside ? "Inside" : "Outside");
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
            _ = plugin.RefreshVenue(venue);
        if (loc is not null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
            {
                Lifestream.Copy(loc);
                plugin.Session.SetAction(venue.Id, "Address copied.");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copy a Lifestream address (no /li). Paste in chat for friends.");
        }
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

        ImGui.AlignTextToFramePadding();
        ImGui.TextWrapped(loc?.AddressPanel ?? loc?.AddressLong ?? here.Long);
        if (!onPlot && loc is not null && Lifestream.Installed() && Reach.CanVisitWorld(loc.World))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Teleport"))
            {
                try { Lifestream.Go(loc); }
                catch (Exception ex) { Plugin.Log.Verbose(ex, "Lifestream"); }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Lifestream: {Lifestream.Share(loc)}");
        }

        DrawLinks(venue);

        ImGui.TextDisabled(FlagsLine(venue));

        if (venue.Description is { Count: > 0 })
        {
            ImGui.Spacing();
            foreach (var para in venue.Description)
            {
                if (string.IsNullOrWhiteSpace(para))
                    continue;
                ImGui.TextWrapped(para.Trim());
            }
        }

        ImGui.TextDisabled(venue.HoursLine);

        ImGui.Spacing();
        DrawCheck(plugin, venue, onPlot, here, inside);

        DrawReportLog(plugin, venue);
        DrawLogBook(plugin, venue, onPlot);
    }

    private static void DrawLinks(VenueListing venue)
    {
        var items = new List<(string Label, Action Click, string Tip)>();
        var partake = !string.IsNullOrWhiteSpace(venue.PartakeUrl);
        var onVenues = !PlaceId.IsPlace(venue.Id);
        if (onVenues)
        {
            items.Add((partake ? "ListingV" : "Listing", () => OpenUrl(Copy.ListingUrl(venue.Id)),
                "Open this venue on FFXIV Venues."));
        }
        if (partake)
            items.Add(("ListingP", () => OpenUrl(venue.PartakeUrl!), "Open this place on Partake."));
        if (!string.IsNullOrWhiteSpace(venue.Discord))
            items.Add(("Discord", () => OpenUrl(venue.Discord!), "Open the Discord invite."));
        if (!string.IsNullOrWhiteSpace(venue.Website)
            && venue.Website.IndexOf("ffxivvenues.com", StringComparison.OrdinalIgnoreCase) < 0)
            items.Add(("Website", () => OpenUrl(venue.Website!), "Open the venue website."));
        items.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            if (ImGui.SmallButton(items[i].Label))
                items[i].Click();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(items[i].Tip);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Dalamud.Utility.Util.OpenLink(url); }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Open link"); }
    }

    private static void DrawCheck(Plugin plugin, VenueListing venue, bool onPlot, HousingAddress here, bool inside)
    {
        if (!NearbyScan.OccupancyEligible(venue))
        {
            ImGui.TextDisabled(Copy.NoPlot);
            return;
        }

        var scanWait = plugin.Session.ScanWait;
        var auditBlocked = !onPlot || scanWait > TimeSpan.Zero;
        if (auditBlocked)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton(scanWait > TimeSpan.Zero
                ? $"Audit ({(int)Math.Ceiling(scanWait.TotalSeconds)}s)"
                : "Audit"))
        {
            var snap = plugin.ScanNow();
            plugin.Session.SetAction(venue.Id, snap.Summary);
        }
        if (auditBlocked)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(!onPlot
                ? (venue.Location?.IsApartment == true
                    ? "Go inside this apartment to audit."
                    : "Stand in the yard or inside this plot. Street of the ward is not enough.")
                : "Local audit. Quiet is always a button. Lanterns may send themselves.");
        }

        var canSend = plugin.CanSend && onPlot;
        ImGui.SameLine();
        DrawSend(plugin, venue, "Active", "happening", canSend, inside,
            !onPlot
                ? "Walk onto this property first. The yard counts."
                : inside ? Copy.HappeningButton : Copy.YardBusy);
        ImGui.SameLine();
        DrawSend(plugin, venue, "Quiet", "wrapped_up", canSend, inside,
            !onPlot
                ? "Walk onto this property first. The yard counts."
                : inside ? Copy.WrappedButton : Copy.YardQuiet);
        if (onPlot && !inside && HousingReader.DoorIsLocked())
        {
            ImGui.SameLine();
            DrawSend(plugin, venue, Copy.DoorLocked, "door_locked", canSend, false);
        }

        if (onPlot && ShowLooksClosed(plugin, here))
        {
            ImGui.SameLine();
            DrawSend(plugin, venue, Copy.LooksClosed, "unhosted", canSend, inside);
        }

        var action = plugin.Session.ActionFor(venue.Id);
        var line = action.Length > 0 ? action : onPlot ? plugin.LastScanLine : "";
        if (line.Length > 0)
            ImGui.TextWrapped(line);
        else if (!onPlot)
            ImGui.TextDisabled(venue.Location?.IsApartment == true
                ? "Go inside this apartment to audit."
                : "Go to this plot to audit. The yard is enough.");

        if (plugin.Session.ObserveSince != default)
        {
            var observe = DateTimeOffset.UtcNow - plugin.Session.ObserveSince;
            if (observe < TimeSpan.FromSeconds(plugin.Session.ObserveSeconds))
                ImGui.TextDisabled($"Watching {plugin.Session.ObserveSeconds - (int)observe.TotalSeconds}s");
        }

        if (plugin.Session.Touring)
            ImGui.TextDisabled($"Touring pace · {Limits.TourSendSeconds}s sends · {Limits.TourScanSeconds}s audits tonight.");
        else if (plugin.Session.TourCount > 0)
            ImGui.TextDisabled($"Touring {plugin.Session.TourCount}/{Limits.TourVenues} listed places tonight.");
    }

    private static bool ShowLooksClosed(Plugin plugin, HousingAddress here)
    {
        var door = !here.OnApartment && !here.Inside
                   && (HousingReader.DoorIsLocked() || plugin.Session.Check.DoorLocked);
        if (door)
            return true;
        var layer = here.Inside || here.OnApartment ? plugin.Session.Check.Inside : plugin.Session.Check.Yard;
        if (layer is null || layer.Value.Patrons <= 0)
            return false;
        var hosted = layer.Value.InCharacter || layer.Value.Glance || layer.Value.Voices
                     || layer.Value.Emotes || plugin.Session.HeardMusic;
        return !hosted;
    }

    private static void DrawSend(Plugin plugin, VenueListing venue, string label, string kind, bool canSend, bool inside, string? tip = null)
    {
        var action = kind == "door_locked"
            ? "door"
            : kind == "unhosted"
                ? "unhosted"
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
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (kind == "unhosted")
                ImGui.SetTooltip("Door locked, or people here without a hosted scene. Staff and plot owner cannot be read from the client. Press twice. Does not replace Quiet Halls when the room is actually empty.");
            else if (!string.IsNullOrWhiteSpace(tip))
                ImGui.SetTooltip(tip);
        }
    }

    private static void DrawReportLog(Plugin plugin, VenueListing venue)
    {
        if (logFor != venue.Id)
        {
            logFor = venue.Id;
            _ = plugin.RefreshLog(venue);
            _ = plugin.RefreshNotes(venue);
        }

        var occ = venue.Occupancy ?? OccupancySnapshot.Unknown;
        var log = venue.Log;
        var exterior = log.Where(e => !e.Inside).Take(Limits.ReportCap).ToList();
        var interior = log.Where(e => e.Inside).Take(Limits.ReportCap).ToList();
        var locked = log.Any(e => e.DoorLocked) || occ.DoorLocked;
        var apartment = venue.Location?.IsApartment == true;

        UiTheme.Gap();
        ImGui.Separator();
        ImGui.TextColored(BadgeColor(occ), SummaryStatus(occ, log, Lean(occ)));

        if (!apartment)
        {
            ImGui.TextColored(UiTheme.Teal, $"Exterior  {Score(exterior)}");
            if (exterior.Count == 0)
                ImGui.TextDisabled("None yet.");
            foreach (var row in exterior)
                ImGui.TextColored(UiTheme.AgeColor(row.At), EventLine(row));
            UiTheme.Gap();
            ImGui.Separator();
        }

        var lockMark = locked && interior.Count > 0 ? "  locked?" : locked ? "  locked" : "";
        ImGui.TextColored(UiTheme.Teal, $"{(apartment ? "Apartment" : "Interior")}{lockMark}  {Score(interior)}");
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
        var noteWait = plugin.Session.NoteWait(venue.Id);
        var ready = onPlot && plugin.Configuration.AllowLogBook && plugin.CanSend
                    && venue.Occupancy?.IsHappening == true
                    && wait <= TimeSpan.Zero
                    && noteWait <= TimeSpan.Zero;
        ImGui.SameLine();
        if (!ready)
            ImGui.BeginDisabled();
        var label = ready
            ? "+ Note"
            : onPlot && venue.Occupancy?.IsHappening == true && noteWait > TimeSpan.Zero
                ? $"+ Note ({FmtWait(noteWait)})"
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
                ImGui.SetTooltip("Go to the listing to leave a note.");
            else if (venue.Occupancy?.IsHappening != true)
                ImGui.SetTooltip("Log book opens when lanterns are lit.");
            else if (wait > TimeSpan.Zero)
                ImGui.SetTooltip($"Stay about {Limits.LogBookDwellMinutes} minutes on the property.");
            else if (noteWait > TimeSpan.Zero)
                ImGui.SetTooltip("One note an hour at this listing.");
            else
                ImGui.SetTooltip("Leave this pair in the log book. Flavor only — not an occupancy point.");
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
