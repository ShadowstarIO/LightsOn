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
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/lightson";
    private const string CommandAlias = "/lo";

    private readonly HttpClient http;
    private readonly DirectoryClient directory;
    private readonly OccupancyClient occupancy;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private CancellationTokenSource refreshCts = new();
    private DateTime lastTick = DateTime.MinValue;
    private DateTime lastPoll = DateTime.MinValue;

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new("LightsOn");
    public readonly Session Session = new();
    public IReadOnlyList<VenueListing> Venues { get; private set; } = [];
    public IReadOnlyList<OutdoorSnapshot> Outdoors { get; private set; } = [];
    public string StatusLine { get; private set; } = "Loading listings…";
    public string LastScanLine { get; private set; } = "No scan yet.";
    public string ActionLine { get; set; } = "";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureReporterId();
        Configuration.Save();

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LightsOn/0.0.2 (+https://github.com/XozaShadow/LightsOn)");
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
        Framework.Update += OnFramework;
        ClientState.TerritoryChanged += OnTerritory;

        if (Configuration.OpenUiOnLoad)
            mainWindow.IsOpen = true;

        _ = RefreshVenues(false);
    }

    public void Dispose()
    {
        Framework.Update -= OnFramework;
        ClientState.TerritoryChanged -= OnTerritory;
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
    public void Notify(string text) => Chat.Print("[LightsOn] " + text);

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
            if (force || Venues.Count == 0)
                StatusLine = "Loading listings…";
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
                    Log.Verbose(ex, "Occupancy fetch failed");
                }

                try
                {
                    Outdoors = await occupancy.GetOutdoors(Configuration.OccupancyApiUrl, token).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Log.Verbose(ex, "Outdoors fetch failed");
                }
            }

            Venues = list;
            StatusLine = $"{list.Count} listed venues";
            lastPoll = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // replaced
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Listing fetch failed");
            StatusLine = "Could not load listings.";
        }
    }

    public async Task RefreshNotes(VenueListing venue)
    {
        if (!OccupancyClient.IsUsable(Configuration.OccupancyApiUrl) || !venue.Occupancy.IsHappening)
        {
            venue.Notes = [];
            return;
        }

        try
        {
            venue.Notes = await occupancy.GetNotes(Configuration.OccupancyApiUrl, venue.Id, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Notes fetch failed");
        }
    }

    public async Task<string> TryReport(VenueListing venue, string kind, bool fromAuto = false)
    {
        if (!Configuration.ReportOptIn)
            return "Turn on Send reports in Settings first.";
        if (!OccupancyClient.IsUsable(Configuration.OccupancyApiUrl))
            return "Set an HTTPS occupancy URL in Settings.";
        if (!ClientState.IsLoggedIn || ObjectTable.LocalPlayer is null)
            return "Not logged in.";
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that plot first. Reports are location-checked.";

        if (kind == "wrapped_up")
        {
            if (DateTimeOffset.UtcNow - Configuration.ReportEnabledAt < TimeSpan.FromMinutes(20))
                return "Send reports was just turned on. Wrapped up early waits 20 minutes.";
            if (Session.OnPlot < TimeSpan.FromMinutes(2.5))
                return "Stay on the plot a couple of minutes first.";
            if (!fromAuto && !Session.WrappedConfirm)
            {
                Session.WrappedConfirm = true;
                return "Press Wrapped up early again to confirm.";
            }
        }

        var scan = ScanNow();
        if (!scan.OnPlot)
            return scan.Summary;

        if (kind == "happening")
        {
            if (!scan.ThresholdMet)
                return "Not enough company after your filters. Nothing sent.";
            if (!scan.Inside)
                return "Step inside, then report lanterns lit. The street cannot see the room.";
        }
        else if (kind == "wrapped_up")
        {
            if (scan.ThresholdMet)
                return "Enough company on the scan. Wrapped up early is blocked.";
            if (scan.Inside)
                return "You are inside and it is quiet. Wrapped up early is for a locked door and empty yard.";
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
            Session.WrappedConfirm = false;
            await RefreshVenues(true).ConfigureAwait(true);
            return kind == "happening" ? "Reported: lanterns are lit." : "Reported: wrapped up early.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Report failed");
            return "Report did not reach the server.";
        }
    }

    public async Task<string> TryNote(VenueListing venue, string text)
    {
        if (!Configuration.AllowLogBook)
            return "Log book is off in Settings.";
        if (!Configuration.ReportOptIn)
            return "Turn on Send reports first.";
        if (!venue.Occupancy.IsHappening)
            return "Log book is only for lanterns lit.";
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that plot first.";
        if (Session.OnPlot < TimeSpan.FromMinutes(20))
            return "Stay about 20 minutes before leaving a note.";
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length < 2 || trimmed.Length > 80)
            return "Keep it between 2 and 80 characters.";

        var scan = ScanNow();
        var here = HousingReader.Read(NearbyScan.CurrentZoneName());
        var post = new NotePost
        {
            VenueId = venue.Id,
            ReporterId = Configuration.ReporterId,
            Text = trimmed,
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
            await occupancy.PostNote(Configuration.OccupancyApiUrl, post, CancellationToken.None).ConfigureAwait(true);
            await RefreshNotes(venue).ConfigureAwait(true);
            return "Note left in the log book.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Note failed");
            return "Note did not reach the server.";
        }
    }

    public async Task<string> TryOutdoor(OutdoorScan scan, bool? privateGathering)
    {
        if (!Configuration.NoteOutdoorScenes || !Configuration.ReportOptIn)
            return "Outdoor notes are off.";
        if (!OccupancyClient.IsUsable(Configuration.OccupancyApiUrl))
            return "No occupancy URL.";
        if (scan.Tier.Length == 0)
            return "Nothing to note.";

        var report = new OutdoorReport
        {
            Pocket = scan.Pocket,
            World = scan.World,
            Place = scan.Place,
            Tier = scan.Tier,
            InCharacter = scan.InCharacter,
            ReporterId = Configuration.ReporterId,
            PrivateGathering = privateGathering,
        };
        try
        {
            await occupancy.PostOutdoor(Configuration.OccupancyApiUrl, report, CancellationToken.None).ConfigureAwait(true);
            Session.LastOutdoorPost = DateTimeOffset.UtcNow;
            Session.OutdoorPrivate = null;
            await RefreshVenues(true).ConfigureAwait(true);
            return privateGathering == true ? "Marked private. It will stay off the list if others agree." : "Outdoor scene noted.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Outdoor report failed");
            return "Outdoor note did not reach the server.";
        }
    }

    private void OnFramework(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if ((now - lastTick).TotalSeconds < 2)
            return;
        lastTick = now;
        if (!ClientState.IsLoggedIn)
            return;

        TickPlot();
        TickOutdoor();

        if ((now - lastPoll).TotalSeconds > 180)
            _ = RefreshVenues(false);
    }

    private void OnTerritory(uint _)
    {
        Session.PocketKey = "";
        Session.PlotKey = "";
        Session.Hop = null;
        Session.OutdoorPrivate = null;
    }

    private void TickPlot()
    {
        var key = NearbyScan.PlotKey();
        if (key != Session.PlotKey)
        {
            Session.PlotKey = key;
            Session.PlotSince = DateTimeOffset.UtcNow;
            Session.HopDismissed = false;
            Session.WrappedConfirm = false;
            Session.Hop = key.Length == 0 ? null : NearbyScan.ListedHere(Venues);
        }

        var venue = Session.Hop;
        if (venue is null || !Configuration.ReportOptIn)
            return;

        if (Configuration.AutoHappening
            && (venue.Id != Session.LastAutoVenue || DateTimeOffset.UtcNow - Session.LastAutoHappening > TimeSpan.FromMinutes(15)))
        {
            var scan = NearbyScan.Run(this);
            LastScanLine = scan.Summary;
            if (scan.Inside && scan.ThresholdMet)
            {
                Session.LastAutoVenue = venue.Id;
                Session.LastAutoHappening = DateTimeOffset.UtcNow;
                _ = AutoHappening(venue);
            }
        }
    }

    private async Task AutoHappening(VenueListing venue)
    {
        var line = await TryReport(venue, "happening", true).ConfigureAwait(true);
        ActionLine = line;
        if (line.StartsWith("Reported", StringComparison.Ordinal))
            Notify($"{venue.Name}: lanterns are lit.");
    }

    private void TickOutdoor()
    {
        if (!Configuration.NoteOutdoorScenes || !Configuration.ReportOptIn)
            return;
        if (Session.PlotKey.Length > 0)
            return;

        var scan = NearbyScan.RunOutdoor(this);
        if (scan.Pocket.Length == 0)
            return;
        if (scan.Pocket != Session.PocketKey)
        {
            Session.PocketKey = scan.Pocket;
            Session.PocketSince = DateTimeOffset.UtcNow;
            Session.OutdoorPrivate = null;
            return;
        }

        if (Session.InPocket < TimeSpan.FromMinutes(10))
            return;
        if (DateTimeOffset.UtcNow - Session.LastOutdoorPost < TimeSpan.FromMinutes(15))
            return;
        if (scan.Tier.Length == 0)
            return;

        if (scan.AskPrivate)
        {
            Session.OutdoorPrivate = new OutdoorPending { Scan = scan };
            return;
        }

        Session.LastOutdoorPost = DateTimeOffset.UtcNow;
        _ = TryOutdoor(scan, null);
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
                Notify("/lo refresh — reload listings");
                break;
            case "config":
                ToggleConfigUi();
                break;
            case "refresh":
                _ = RefreshVenues(true);
                Notify("Refreshing listings…");
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
