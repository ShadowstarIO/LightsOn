using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LightsOn.Api;

namespace LightsOn.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("LightsOn · settings###LightsOnConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(480, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 360),
            MaximumSize = new Vector2(640, 860),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        UiTheme.Section("Reports");
        ImGui.TextWrapped(Copy.ReportsBlurb);

        var optIn = cfg.ReportOptIn;
        if (ImGui.Checkbox("Send reports", ref optIn))
        {
            cfg.ReportOptIn = optIn;
            cfg.HasSeenWelcome = true;
            cfg.Save();
        }

        var friends = cfg.ExcludeFriends;
        if (ImGui.Checkbox("Leave friends out of company", ref friends))
        {
            cfg.ExcludeFriends = friends;
            cfg.Save();
        }

        var fc = cfg.ExcludeFreeCompany;
        if (ImGui.Checkbox("Leave Free Company out of company", ref fc))
        {
            cfg.ExcludeFreeCompany = fc;
            cfg.Save();
        }

        ImGui.Separator();
        UiTheme.Section("What the lantern reads");
        ImGui.TextWrapped("Sits between OOC and IC. This build scores patrons only.");
        Line("Patron", "Another person on the plot. In-game: other PCs. 1 each, cap 3.");
        Line("In character", "Role-Playing status. Later.");
        Line("Seeking company", "Looking for Party. Later.");
        Line("A gathering is posted", "Party Finder. Later.");
        Line("At the bench", "Melding Materia. Later.");
        Line("Someone reached out", "A /tell from someone on this plot. Later.");
        Line("An exchange", "A trade here. Later.");
        Line("Voices nearby", "Say from this plot. Off unless you turn it on. Later.");
        ImGui.TextDisabled("Enough company = 3 patrons after filters. Quiet = not that, plus the door/yard.");

        ImGui.Separator();
        UiTheme.Section("Server");
        ImGui.TextDisabled("HTTPS hostname. Empty = listings only.");
        var url = cfg.OccupancyApiUrl;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##api", ref url, 256))
        {
            cfg.OccupancyApiUrl = url.Trim();
            cfg.Save();
        }

        if (url.Length > 0 && !OccupancyClient.IsUsable(url))
            ImGui.TextColored(UiTheme.Amber, "Need https:// and a DNS hostname, not an IP.");

        ImGui.Separator();
        UiTheme.Section("Reporter id");
        ImGui.TextWrapped("Random. Not your character. Reset any time.");
        ImGui.TextDisabled(ShortId(cfg.ReporterId));
        if (ImGui.Button("Reset reporter id"))
        {
            cfg.ResetReporterId();
            cfg.Save();
        }

        ImGui.Separator();
        var open = cfg.OpenUiOnLoad;
        if (ImGui.Checkbox("Open window on login", ref open))
        {
            cfg.OpenUiOnLoad = open;
            cfg.Save();
        }
    }

    private static void Line(string title, string body)
    {
        ImGui.TextColored(UiTheme.Teal, title);
        ImGui.SameLine();
        ImGui.TextWrapped(body);
    }

    private static string ShortId(string id)
        => id.Length <= 12 ? id : id[..8] + "…" + id[^4..];
}
