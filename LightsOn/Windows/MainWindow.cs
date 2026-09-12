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
    private string actionLine = "";

    private static readonly string[] Filters = ["All", "Marked open", "Lanterns lit"];

    public MainWindow(Plugin plugin)
        : base("LightsOn###LightsOnMain")
    {
        this.plugin = plugin;
        Size = new Vector2(720, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 360),
            MaximumSize = new Vector2(1100, 900),
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
            var occ = venue.Occupancy;
            var mark = occ.IsHappening ? "● " : occ.IsWrappedUp ? "○ " : "  ";
            var label = $"{mark}{venue.Name}##{venue.Id}";
            var isSel = venue.Id == selectedId;
            if (ImGui.Selectable(label, isSel))
                selectedId = venue.Id;
            ImGui.SameLine();
            if (occ.IsHappening)
                ImGui.TextColored(UiTheme.Happening, HappeningLabel(occ));
            else if (occ.IsWrappedUp)
                ImGui.TextColored(UiTheme.Wrapped, WrappedLabel(occ));
            else if (venue.Resolution?.IsNow == true)
                ImGui.TextDisabled(Copy.MarkedOpen);
            if (loc is not null)
                ImGui.TextDisabled(loc.Address);
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
        var occ = venue.Occupancy;
        ImGui.TextUnformatted(venue.Name);
        if (loc is not null)
            ImGui.TextDisabled(loc.Address);
        ImGui.TextDisabled(venue.Sfw ? "SFW listing" : "NSFW listing");

        ImGui.Spacing();
        if (occ.IsHappening)
        {
            ImGui.TextColored(UiTheme.Happening, Copy.Happening);
            ImGui.TextDisabled(HappeningLabel(occ));
        }
        else if (occ.IsWrappedUp)
        {
            ImGui.TextColored(UiTheme.Wrapped, Copy.Wrapped);
            ImGui.TextDisabled(WrappedLabel(occ));
        }
        else if (venue.Resolution?.IsNow == true)
        {
            ImGui.TextColored(UiTheme.Amber, $"{Copy.MarkedOpen} — {Copy.NoReport}");
        }
        else
        {
            ImGui.TextDisabled(Copy.NoReport);
        }

        ImGui.Spacing();
        UiTheme.Section("On this plot", true);
        ImGui.TextWrapped(plugin.LastScanLine);

        var onPlot = loc is not null && NearbyScan.MatchesVenue(venue);
        if (!onPlot)
            ImGui.TextDisabled("Travel to the plot to report.");

        var canReport = onPlot && plugin.Configuration.ReportOptIn;
        if (!canReport)
            ImGui.BeginDisabled();
        if (ImGui.Button(Copy.HappeningButton))
            _ = Report(venue, "happening");
        ImGui.SameLine();
        if (ImGui.Button(Copy.WrappedButton))
            _ = Report(venue, "wrapped_up");
        if (!canReport)
            ImGui.EndDisabled();

        if (!plugin.Configuration.ReportOptIn)
            ImGui.TextDisabled("Settings → Send reports.");
        if (actionLine.Length > 0)
            ImGui.TextWrapped(actionLine);
    }

    private async System.Threading.Tasks.Task Report(VenueListing venue, string kind)
    {
        actionLine = "Scanning…";
        actionLine = await plugin.TryReport(venue, kind).ConfigureAwait(true);
    }

    private bool Matches(VenueListing venue)
    {
        if (filter == 1 && venue.Resolution?.IsNow != true)
            return false;
        if (filter == 2 && !venue.Occupancy.IsHappening)
            return false;
        if (query.Length == 0)
            return true;
        var loc = venue.Location;
        return venue.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (loc?.World.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (loc?.DataCenter.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (loc?.District.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

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
