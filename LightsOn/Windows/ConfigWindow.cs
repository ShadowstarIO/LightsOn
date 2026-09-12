using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

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
        UiTheme.Section("Connection");
        var listings = cfg.ListingsOnly;
        if (ImGui.Checkbox("Listings only", ref listings))
        {
            cfg.ListingsOnly = listings;
            cfg.Save();
            _ = plugin.RefreshVenues(true);
        }
        ImGui.TextDisabled("On: hours only, nothing is fetched or sent. Off: occupancy is used.");

        ImGui.Separator();
        UiTheme.Section("Reports");
        ImGui.TextWrapped(Copy.ReportsBlurb);

        var optIn = cfg.ReportOptIn;
        if (cfg.ListingsOnly)
            ImGui.BeginDisabled();
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
        if (cfg.ListingsOnly)
            ImGui.EndDisabled();

        var prompt = cfg.PromptOnEnter;
        if (ImGui.Checkbox("Prompt when you walk onto a listed plot", ref prompt))
        {
            cfg.PromptOnEnter = prompt;
            cfg.Save();
        }

        ImGui.TextDisabled("Wrapped up early always asks twice, needs a few minutes on the plot, and waits 20 minutes after you turn reports on. Occupancy is one report per 20 minutes.");

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

        var glance = cfg.UseGlanceSignals;
        if (ImGui.Checkbox("Count a glance (looking at / looked at) as extra company", ref glance))
        {
            cfg.UseGlanceSignals = glance;
            cfg.Save();
        }

        var chat = cfg.UseChatSignals;
        if (ImGui.Checkbox("Count tells and party chat with patrons here as extra company", ref chat))
        {
            cfg.UseChatSignals = chat;
            cfg.Save();
        }

        var say = cfg.UseSaySignals;
        if (ImGui.Checkbox("Count say with patrons here (voices nearby)", ref say))
        {
            cfg.UseSaySignals = say;
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
        Line("A glance", "You or a patron has the other targeted. +1 total.");
        Line("Someone reached out", "Tell or party chat with a patron here. +1 total.");
        Line("Voices nearby", "Say with a patron here. Off unless you turn it on. +1 total.");
        ImGui.TextDisabled("Enough company = score 3 on that layer. A property check needs the yard and the inside (or a locked door). Apartments are listed, not checked.");

        ImGui.Separator();
        UiTheme.Section("Reporter id");
        ImGui.TextWrapped("Random. Not your character. After a reset, reports wait 20 minutes.");
        ImGui.TextDisabled(ShortId(cfg.ReporterId));
        if (ImGui.Button("Reset reporter id"))
        {
            cfg.ResetReporterId();
            cfg.Save();
        }
        var lockLeft = cfg.ReporterResetLockRemaining;
        if (lockLeft > TimeSpan.Zero)
        {
            var mins = Math.Max(1, (int)Math.Ceiling(lockLeft.TotalMinutes));
            ImGui.TextColored(UiTheme.Amber, $"Reports locked for {mins} more minute{(mins == 1 ? "" : "s")}.");
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
