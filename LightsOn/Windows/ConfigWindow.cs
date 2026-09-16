using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LightsOn.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private bool resetArmed;

    public ConfigWindow(Plugin plugin)
        : base("LightsOn · Settings###LightsOnConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 420),
            MaximumSize = new Vector2(660, 980),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        UiTheme.Section("What the Lantern Reads");
        Line("Patrons", "People in the area. 1 each, cap 3.");
        Line("IC", "Role-Playing / in character. +1.");
        Line("Party Finder", "Looking for Party is up. +1.");
        Line("Melding", "This status is on a player. +1. How they use it is their business.");
        Line("Glances", "You looked at someone, they looked at you, or they looked at each other. +1 total.");
        Line("Emotes", "A patron is emoting, looping an emote, or one fired in chat. +1 total. Verbose log not required.");
        Line("Contact", "Tell or party chat with a patron here. +1 total.");
        Line("Voices", "Say with a patron here. Off unless you turn it on. +1 total.");
        ImGui.TextDisabled("Enough company = score 3 on that layer. Yard is about one plot-edge. The room is everyone inside.");

        UiTheme.Gap();
        ImGui.Separator();
        UiTheme.Section("Settings");

        var listings = cfg.ListingsOnly;
        if (ImGui.Checkbox("Listings Only", ref listings))
        {
            cfg.ListingsOnly = listings;
            cfg.Save();
            _ = plugin.RefreshVenues(true);
        }
        UiTheme.Hint("Hours only. Occupancy is not fetched or sent.");

        var optIn = cfg.ReportOptIn;
        if (cfg.ListingsOnly)
            ImGui.BeginDisabled();
        if (ImGui.Checkbox("Send Reports", ref optIn))
        {
            cfg.SetReportOptIn(optIn);
            cfg.HasSeenWelcome = true;
            cfg.Save();
        }
        UiTheme.Hint("Active and Quiet send occupancy. Names and counts never leave. Off means browse only.");

        var auto = cfg.AutoHappening;
        if (ImGui.Checkbox("Auto Audit Routine", ref auto))
        {
            cfg.AutoHappening = auto;
            cfg.Save();
        }
        UiTheme.Hint("After a short stay, send lanterns when the local audit is enough. Off unless you turn it on. Quiet is never automatic. Active and Quiet still work as buttons.");
        if (cfg.ListingsOnly)
            ImGui.EndDisabled();

        var prompt = cfg.PromptOnEnter;
        if (ImGui.Checkbox("Open Plot Window On Enter", ref prompt))
        {
            cfg.PromptOnEnter = prompt;
            cfg.Save();
        }
        UiTheme.Hint("Opens Current Plot when you walk onto an open listed venue.");

        var closeLeave = cfg.ClosePlotOnLeave;
        if (ImGui.Checkbox("Close Plot Window On Leave", ref closeLeave))
        {
            cfg.ClosePlotOnLeave = closeLeave;
            cfg.Save();
        }
        UiTheme.Hint("Closes Current Plot when you leave the property.");

        var open = cfg.OpenUiOnLoad;
        if (ImGui.Checkbox("Open On Login", ref open))
        {
            cfg.OpenUiOnLoad = open;
            cfg.Save();
        }
        UiTheme.Hint("Opens the main window when you log in.");

        var friends = cfg.ExcludeFriends;
        if (ImGui.Checkbox("Leave Friends Out", ref friends))
        {
            cfg.ExcludeFriends = friends;
            cfg.Save();
        }
        UiTheme.Hint("Friends are not counted as company.");

        var fc = cfg.ExcludeFreeCompany;
        if (ImGui.Checkbox("Leave Free Company Out", ref fc))
        {
            cfg.ExcludeFreeCompany = fc;
            cfg.Save();
        }
        UiTheme.Hint("Free Company members are not counted as company.");

        var status = cfg.UseStatusSignals;
        if (ImGui.Checkbox("Count IC, Party Finder, and Melding", ref status))
        {
            cfg.UseStatusSignals = status;
            cfg.Save();
        }
        UiTheme.Hint("Each of these statuses on a patron is extra company.");

        var glance = cfg.UseGlanceSignals;
        if (ImGui.Checkbox("Count Glances", ref glance))
        {
            cfg.UseGlanceSignals = glance;
            cfg.Save();
        }
        UiTheme.Hint("Looking at / looked at a patron. +1 total.");

        var emotes = cfg.UseEmoteSignals;
        if (ImGui.Checkbox("Count Emotes", ref emotes))
        {
            cfg.UseEmoteSignals = emotes;
            cfg.Save();
        }
        UiTheme.Hint("Looping or in-place emotes, and emote chat. +1 total. Verbose log not required.");

        var chat = cfg.UseChatSignals;
        if (ImGui.Checkbox("Count Contact", ref chat))
        {
            cfg.UseChatSignals = chat;
            cfg.Save();
        }
        UiTheme.Hint("Tell or party chat with a patron here. +1 total.");

        var say = cfg.UseSaySignals;
        if (ImGui.Checkbox("Count Voices", ref say))
        {
            cfg.UseSaySignals = say;
            cfg.Save();
        }
        UiTheme.Hint("Say with a patron here. Off by default.");

        var book = cfg.AllowLogBook;
        if (ImGui.Checkbox("Allow Log Book", ref book))
        {
            cfg.AllowLogBook = book;
            cfg.Save();
        }
        UiTheme.Hint("Short pair from two lists while lanterns are lit, after a wait on the property.");

        var outdoors = cfg.NoteOutdoorScenes;
        if (ImGui.Checkbox("Report Outdoor Scenes", ref outdoors))
        {
            cfg.NoteOutdoorScenes = outdoors;
            cfg.Save();
        }
        UiTheme.Hint("Street pockets. Audit and stay, or wait about 10 minutes. Same area waits 5 minutes.");

        UiTheme.Gap();
        ImGui.Separator();
        UiTheme.Section("Reporter ID");
        UiTheme.Hint("Random. Not your character.");
        ImGui.TextUnformatted(cfg.ReporterId);
        ImGui.SameLine();
        var lockLeft = cfg.ReporterResetLockRemaining;
        if (lockLeft > TimeSpan.Zero)
        {
            var mins = Math.Max(1, (int)Math.Ceiling(lockLeft.TotalMinutes));
            ImGui.BeginDisabled();
            ImGui.SmallButton($"Locked {mins}m");
            ImGui.EndDisabled();
            resetArmed = false;
        }
        else if (resetArmed)
        {
            if (ImGui.SmallButton("Confirm Reset"))
            {
                cfg.ResetReporterId();
                cfg.Save();
                resetArmed = false;
            }
        }
        else if (ImGui.SmallButton("Reset ID"))
            resetArmed = true;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Reports and notes pause for 20 minutes after a reset.");
    }

    private static void Line(string title, string body)
    {
        ImGui.TextColored(UiTheme.Teal, title);
        ImGui.SameLine();
        ImGui.TextWrapped(body);
    }
}
