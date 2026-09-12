using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LightsOn.Api;
using LightsOn.Scan;
using LightsOn.Windows;

namespace LightsOn;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/lightson";
    private const string CommandAlias = "/lo";

    private readonly HttpClient http;
    private readonly DirectoryClient directory;
    private readonly OccupancyClient occupancy;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private CancellationTokenSource refreshCts = new();

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new("LightsOn");
    public IReadOnlyList<VenueListing> Venues { get; private set; } = [];
    public string StatusLine { get; private set; } = "Loading venues…";
    public string LastScanLine { get; private set; } = "No scan yet. Scan only runs when you report.";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureReporterId();
        Configuration.Save();

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LightsOn/0.0.1 (+https://github.com/XozaShadow/LightsOn)");
        directory = new DirectoryClient(http);
        occupancy = new OccupancyClient(http);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "LightsOn. /lo here · /lo config",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /lightson.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        if (Configuration.OpenUiOnLoad)
            mainWindow.IsOpen = true;

        _ = RefreshVenues(false);
    }

    public void Dispose()
    {
        refreshCts.Cancel();
        refreshCts.Dispose();
        http.Dispose();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
    }

    public void ToggleConfigUi() => configWindow.Toggle();
    public void ToggleMainUi() => mainWindow.Toggle();

    public void Notify(string text) => Chat.Print(text);

    public ScanResult ScanNow()
    {
        var result = NearbyScan.Run(this);
        LastScanLine = result.Summary;
        return result;
    }

    public async Task RefreshVenues(bool force)
    {
        refreshCts.Cancel();
        refreshCts.Dispose();
        refreshCts = new CancellationTokenSource();
        var token = refreshCts.Token;
        try
        {
            StatusLine = "Loading venues…";
            var list = await directory.GetVenues(force, token).ConfigureAwait(true);
            if (OccupancyClient.IsUsable(Configuration.OccupancyApiUrl))
            {
                try
                {
                    var map = await occupancy.GetOccupancy(Configuration.OccupancyApiUrl, token).ConfigureAwait(true);
                    foreach (var venue in list)
                    {
                        if (map.TryGetValue(venue.Id, out var snap))
                            venue.Occupancy = snap;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Occupancy fetch failed");
                }
            }

            Venues = list;
            StatusLine = $"{list.Count} listed venues";
        }
        catch (OperationCanceledException)
        {
            // replaced
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Venue list fetch failed");
            StatusLine = "Could not load FFXIV Venues list.";
        }
    }

    public async Task<string> TryReport(VenueListing venue, string kind)
    {
        if (!Configuration.ReportOptIn)
            return "Turn on Send reports in Settings first.";
        if (!OccupancyClient.IsUsable(Configuration.OccupancyApiUrl))
            return "Set an HTTPS occupancy API URL in Settings.";
        if (!ClientState.IsLoggedIn || ObjectTable.LocalPlayer is null)
            return "Not logged in.";
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that plot first. Reports are location-checked.";

        var scan = ScanNow();
        if (!scan.OnPlot)
            return scan.Summary;

        if (kind == "happening")
        {
            if (!scan.ThresholdMet)
                return "Scan did not hit 3+ (after your filters). Nothing sent.";
            if (!scan.Inside)
                return "Step inside, then report happening. The street cannot see the room.";
        }
        else if (kind == "wrapped_up")
        {
            if (scan.ThresholdMet)
                return "Scan hit 3+. Wrapped-up is blocked.";
            if (scan.Inside)
                return "You are inside with under 3 after filters. Wrapped-up is for a locked door and empty yard.";
        }
        else
        {
            return "Unknown report kind.";
        }

        var here = HousingReader.Read(NearbyScan.CurrentZoneName());
        var report = new OccupancyReport
        {
            VenueId = venue.Id,
            Kind = kind,
            ReporterId = Configuration.ReporterId,
            At = DateTimeOffset.UtcNow,
            Proof =
            {
                World = NearbyScan.CurrentWorldName(),
                District = here.District,
                Ward = here.Ward,
                Plot = here.Plot,
                Subdivision = here.Subdivision,
                Inside = scan.Inside,
                ThresholdMet = scan.ThresholdMet,
            },
        };

        try
        {
            await occupancy.PostReport(Configuration.OccupancyApiUrl, report, CancellationToken.None).ConfigureAwait(true);
            await RefreshVenues(true).ConfigureAwait(true);
            return kind == "happening" ? "Reported: something's happening." : "Reported: wrapped up early.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Report failed");
            return "Report did not reach the server.";
        }
    }

    private void OnCommand(string command, string args)
    {
        var key = (args ?? "").Trim().ToLowerInvariant();
        switch (key)
        {
            case "help":
                Notify("/lo — open LightsOn");
                Notify("/lo here — current plot");
                Notify("/lo config — settings");
                Notify("/lo refresh — reload venue list");
                break;
            case "config":
                ToggleConfigUi();
                break;
            case "refresh":
                _ = RefreshVenues(true);
                Notify("Refreshing venues…");
                break;
            case "here":
            {
                if (!ClientState.IsLoggedIn)
                {
                    Notify("Not logged in.");
                    break;
                }
                var here = HousingReader.Read(NearbyScan.CurrentZoneName());
                Notify($"{NearbyScan.CurrentWorldName()} · {here.Summary}");
                break;
            }
            default:
                ToggleMainUi();
                break;
        }
    }
}
