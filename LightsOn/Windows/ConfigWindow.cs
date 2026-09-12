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
        Size = new Vector2(500, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 400),
            MaximumSize = new Vector2(660, 900),
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
            cfg.SetReportOptIn(optIn);
            cfg.HasSeenWelcome = true;
            cfg.Save();
        }

        var auto = cfg.AutoHappening;
        if (ImGui.Checkbox("Auto lanterns-lit when enough company inside", ref auto))
        {
            cfg.AutoHappening = auto;
            cfg.Save();
        }

        var prompt = cfg.PromptOnEnter;
        if (ImGui.Checkbox("Prompt when you walk onto a listed plot", ref prompt))
        {
            cfg.PromptOnEnter = prompt;
            cfg.Save();
        }

        ImGui.TextDisabled("Wrapped up early always asks twice, needs a few minutes on the plot, and waits 20 minutes after you turn reports on.");

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

        var status = cfg.UseStatusSignals;
        if (ImGui.Checkbox("Count in-character / seeking company / at the bench as extra company", ref status))
        {
            cfg.UseStatusSignals = status;
            cfg.Save();
        }

        ImGui.Separator();
        UiTheme.Section("Log book");
        ImGui.TextWrapped(Copy.LogBookHint);
        var book = cfg.AllowLogBook;
        if (ImGui.Checkbox("Allow log-book notes", ref book))
        {
            cfg.AllowLogBook = book;
            cfg.Save();
        }

        ImGui.Separator();
        UiTheme.Section("Outdoors");
        ImGui.TextWrapped(Copy.OutdoorsHint);
        var outdoors = cfg.NoteOutdoorScenes;
        if (ImGui.Checkbox("Note outdoor scenes", ref outdoors))
        {
            cfg.NoteOutdoorScenes = outdoors;
            cfg.Save();
        }

        ImGui.Separator();
        UiTheme.Section("What the lantern reads");
        Line("Patron", "Another person on the plot. 1 each, cap 3.");
        Line("In character", "Role-Playing status. +1.");
        Line("Seeking company", "Looking for Party / recruiting. +1.");
        Line("At the bench", "Melding Materia. +1.");
        Line("Someone reached out", "/tell — not scored in this testing build.");
        Line("An exchange", "Trade — not scored in this testing build.");
        Line("Voices nearby", "Say — off. Not scored.");
        ImGui.TextDisabled("Enough company = score 3. Quiet = under that, plus the door/yard.");

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
