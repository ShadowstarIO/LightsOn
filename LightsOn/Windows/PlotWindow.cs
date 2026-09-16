using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LightsOn.Scan;

namespace LightsOn.Windows;

public sealed class PlotWindow : Window
{
    private readonly Plugin plugin;

    public PlotWindow(Plugin plugin)
        : base("LightsOn · Current Plot###LightsOnPlot")
    {
        this.plugin = plugin;
        Size = new Vector2(420, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 280),
            MaximumSize = new Vector2(640, 900),
        };
    }

    public override void Draw()
    {
        UiTheme.DrawHere(plugin.Session.HereLine, false);

        var venue = plugin.Session.Hop ?? NearbyScan.ListedHere(plugin.Venues);
        if (venue is null)
        {
            ImGui.TextDisabled("Not on a listed plot or apartment.");
            if (ImGui.SmallButton("Full"))
                plugin.ToggleMainUi();
            return;
        }

        UiTheme.Gap();
        ImGui.Separator();
        VenueView.Draw(plugin, venue, true);
    }
}
