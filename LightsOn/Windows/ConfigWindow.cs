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
        Size = new Vector2(460, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 280),
            MaximumSize = new Vector2(640, 720),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        UiTheme.Section("Reports");
        ImGui.TextWrapped("Reports are off until you opt in. The plugin still never sends names, IDs, or a player count.");

        var optIn = cfg.ReportOptIn;
        if (ImGui.Checkbox("Send reports", ref optIn))
        {
            cfg.ReportOptIn = optIn;
            cfg.HasSeenWelcome = true;
            cfg.Save();
        }

        var friends = cfg.ExcludeFriends;
        if (ImGui.Checkbox("Leave friends out of the 3+ check", ref friends))
        {
            cfg.ExcludeFriends = friends;
            cfg.Save();
        }

        var fc = cfg.ExcludeFreeCompany;
        if (ImGui.Checkbox("Leave Free Company members out of the 3+ check", ref fc))
        {
            cfg.ExcludeFreeCompany = fc;
            cfg.Save();
        }

        ImGui.Separator();
        UiTheme.Section("Server");
        ImGui.TextDisabled("HTTPS hostname. Empty = browse listings only, no occupancy.");
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

    private static string ShortId(string id)
        => id.Length <= 12 ? id : id[..8] + "…" + id[^4..];
}
