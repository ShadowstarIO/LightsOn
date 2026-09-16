using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
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

    public const string Version = "0.0.4.14";
    public const string OccupancyHost = "https://lightson.shadowstar.io";
    private const string CommandName = "/lightson";
    private const string CommandAlias = "/lon";

    private readonly HttpClient http;
    private readonly DirectoryClient directory;
    private readonly OccupancyClient occupancy;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly PlotWindow plotWindow;
    private CancellationTokenSource refreshCts = new();
    private DateTime lastTick = DateTime.MinValue;
    private DateTime lastHere = DateTime.MinValue;
    private DateTime lastPoll = DateTime.MinValue;

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new("LightsOn");
    public readonly Session Session = new();
    public IReadOnlyList<VenueListing> Venues { get; private set; } = [];
    public IReadOnlyList<OutdoorSnapshot> Outdoors { get; private set; } = [];
    public string StatusLine { get; private set; } = "Loading listings…";
    public string LastScanLine { get; private set; } = "No audit yet.";
    public string ActionLine { get; set; } = "";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureReporterId();
        if (string.IsNullOrWhiteSpace(Configuration.OccupancyApiUrl)
            || !Configuration.OccupancyApiUrl.StartsWith(OccupancyHost, StringComparison.OrdinalIgnoreCase))
            Configuration.OccupancyApiUrl = OccupancyHost;
        RefreshTourPace();
        Configuration.Save();

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LightsOn/0.0.4.14 (+https://github.com/ShadowstarIO/LightsOn)");
        directory = new DirectoryClient(http);
        occupancy = new OccupancyClient(http);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        plotWindow = new PlotWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(plotWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "LightsOn. /lon here · /lon plot · /lon config",
        });
        try
        {
            CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
            {
                HelpMessage = "Alias for /lightson.",
            });
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Could not register /lon");
        }

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFramework;
        ClientState.TerritoryChanged += OnTerritory;
        Chat.ChatMessage += OnChat;

        if (Configuration.OpenUiOnLoad)
            mainWindow.IsOpen = true;

        _ = RefreshVenues(false);
    }

    public void Dispose()
    {
        Chat.ChatMessage -= OnChat;
        Framework.Update -= OnFramework;
        ClientState.TerritoryChanged -= OnTerritory;
        refreshCts.Cancel();
        refreshCts.Dispose();
        http.Dispose();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        WindowSystem.RemoveAllWindows();
        try { CommandManager.RemoveHandler(CommandName); } catch { /* already gone */ }
        try { CommandManager.RemoveHandler(CommandAlias); } catch { /* already gone */ }
    }

    public void ToggleConfigUi() => configWindow.Toggle();
    public void ToggleMainUi() => mainWindow.Toggle();
    public void TogglePlotWindow() => plotWindow.IsOpen = !plotWindow.IsOpen;
    public void OpenPlotWindow() => plotWindow.IsOpen = true;
    public void SelectVenue(string id) => mainWindow.Select(id);
    public void ShowMini() => plotWindow.IsOpen = true;
    public void ShowFull(string id)
    {
        mainWindow.Select(id);
        mainWindow.IsOpen = true;
    }
    public bool MainUiOpen => mainWindow.IsOpen;
    public bool PlotUiOpen => plotWindow.IsOpen;
    public void Notify(string text) => Chat.Print("[LightsOn] " + text);
    public bool CanSend => SendBlock() is null;

    public void RefreshTourPace()
    {
        Configuration.TrimTour();
        Session.TourCount = Configuration.TourVenues.Count;
        Session.SendSeconds = Session.Touring ? Limits.TourSendSeconds : Limits.SendRateSeconds;
        Session.ScanSeconds = Session.Touring ? Limits.TourScanSeconds : Limits.ScanCooldownSeconds;
        Session.ObserveSeconds = Session.Touring ? Limits.TourObserveSeconds : Limits.ObserveSeconds;
    }

    public async Task ReportUi(VenueListing venue, string kind)
    {
        if (Session.Sending)
            return;
        ActionLine = "Sending…";
        var line = await TryReport(venue, kind).ConfigureAwait(true);
        ActionLine = line;
        Session.SetAction(venue.Id, line);
    }

    public async Task LeaveNoteUi(VenueListing venue, string text)
    {
        ActionLine = await TryNote(venue, text).ConfigureAwait(true);
        Session.SetAction(venue.Id, ActionLine);
    }

    public ScanResult ScanNow(bool markAudit = true)
    {
        var result = NearbyScan.Run(this);
        LastScanLine = result.Summary;
        if (markAudit)
            Session.LastScanAt = DateTimeOffset.UtcNow;
        if (result.OnPlot)
            Session.Check.Absorb(result);
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
            if (Configuration.OccupancyEnabled)
            {
                try
                {
                    var map = await occupancy.GetOccupancy(Configuration.OccupancyApiUrl, token).ConfigureAwait(true);
                    foreach (var venue in list)
                    {
                        if (venue?.Id is not { Length: > 0 })
                            continue;
                        venue.Occupancy ??= OccupancySnapshot.Unknown;
                        if (map.TryGetValue(venue.Id, out var snap) && snap is not null)
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
            else
            {
                foreach (var venue in list)
                {
                    if (venue is not null)
                        venue.Occupancy = OccupancySnapshot.Unknown;
                }
                Outdoors = [];
            }

            var previous = Venues;
            foreach (var venue in list)
            {
                var old = previous.FirstOrDefault(v => v.Id == venue.Id);
                if (old is null)
                    continue;
                if (venue.Log.Count == 0 && old.Log.Count > 0)
                    venue.Log = old.Log;
                if (venue.Notes.Count == 0 && old.Notes.Count > 0)
                    venue.Notes = old.Notes;
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

    public async Task RefreshVenue(VenueListing venue)
    {
        await RefreshOccupancy().ConfigureAwait(true);
        await RefreshLog(venue).ConfigureAwait(true);
        await RefreshNotes(venue).ConfigureAwait(true);
    }

    public async Task RefreshOccupancy()
    {
        if (!Configuration.OccupancyEnabled)
            return;
        try
        {
            var map = await occupancy.GetOccupancy(Configuration.OccupancyApiUrl, CancellationToken.None).ConfigureAwait(true);
            foreach (var venue in Venues)
            {
                if (map.TryGetValue(venue.Id, out var snap) && snap is not null)
                    venue.Occupancy = snap;
            }
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Occupancy refresh failed");
        }
    }

    public async Task RefreshNotes(VenueListing venue)
    {
        if (!Configuration.OccupancyEnabled)
            return;

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

    public async Task RefreshLog(VenueListing venue)
    {
        if (!Configuration.OccupancyEnabled)
        {
            venue.Log = [];
            return;
        }

        try
        {
            venue.Log = await occupancy.GetReportLog(Configuration.OccupancyApiUrl, venue.Id, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Report log fetch failed");
            venue.Log = [];
        }
    }

    public async Task<string> TryReport(VenueListing venue, string kind, bool fromAuto = false)
    {
        if (Session.Sending)
            return Session.ActionFor(venue.Id) is { Length: > 0 } busy ? busy : "Sending…";
        Session.Sending = true;
        try
        {
            return await TryReportCore(venue, kind, fromAuto).ConfigureAwait(true);
        }
        finally
        {
            Session.Sending = false;
        }
    }

    private async Task<string> TryReportCore(VenueListing venue, string kind, bool fromAuto)
    {
        if (SendBlock() is { } blocked)
            return blocked;
        if (!ClientState.IsLoggedIn || ObjectTable.LocalPlayer is null)
            return "Not logged in.";
        if (!NearbyScan.OccupancyEligible(venue))
            return Copy.NoPlot;
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that listing first. Reports are location-checked.";
        if (venue.Resolution?.IsNow != true)
            return "Directory does not show this listing as open right now. Nothing sent.";

        var scan = ScanNow(false);
        if (!scan.OnPlot)
            return scan.Summary;

        var requested = kind;
        var unhosted = requested == "unhosted";
        var doorLocked = requested == "door_locked" || (!scan.Inside && Session.Check.DoorLocked);
        if (requested == "door_locked")
        {
            if (scan.Inside)
                return "Mark the door from the yard.";
            kind = scan.ThresholdMet ? "happening" : "wrapped_up";
            doorLocked = true;
        }

        if (requested == "unhosted")
        {
            kind = "wrapped_up";
            doorLocked = doorLocked && !scan.Inside;
        }

        if (kind != "happening" && kind != "wrapped_up")
            return "Unknown report kind.";

        var action = requested == "door_locked" ? "door" : requested == "unhosted" ? "unhosted" : SendAction(kind, scan.Inside, false);
        var wait = Session.SendWait(venue.Id, action);
        if (wait > TimeSpan.Zero)
            return $"Already sent. Try again in {(int)Math.Ceiling(wait.TotalSeconds)}s.";

        if (kind == "happening")
        {
            if (!scan.ThresholdMet && requested != "door_locked")
                return "Not enough company after your filters. Nothing sent.";
        }
        else
        {
            if (scan.ThresholdMet && requested != "door_locked" && !unhosted)
                return "Enough company on this layer. Quiet is blocked.";
            if (!fromAuto && requested != "door_locked" && Session.WrapSureVenue != venue.Id)
            {
                Session.WrapSureVenue = venue.Id;
                if (unhosted)
                    return "Looks unhosted — visitors, no hosted scene you can tell. Press again to send.";
                return scan.Inside
                    ? "Are you sure the halls are quiet? Press again to send."
                    : "Are you sure the yard is quiet? Press again to send.";
            }
        }

        var here = HousingReader.Read();
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
                Apartment = here.Apartment,
                Subdivision = here.Subdivision,
                Inside = scan.Inside,
                ThresholdMet = scan.ThresholdMet,
                DoorLocked = doorLocked && !scan.Inside,
                Unhosted = unhosted,
                Voices = scan.Voices,
                Glance = scan.Glance,
                Music = Session.HeardMusic,
            },
        };

        try
        {
            await occupancy.PostReport(Configuration.OccupancyApiUrl, report, CancellationToken.None).ConfigureAwait(true);
            Session.WrapSureVenue = null;
            Session.MarkSent(venue.Id, action);
            Configuration.MarkTour(venue.Id);
            RefreshTourPace();
            await RefreshOccupancy().ConfigureAwait(true);
            await RefreshLog(venue).ConfigureAwait(true);
            var line = kind == "happening"
                ? (scan.Inside ? "Reported: lanterns are lit." : "Reported: yard is busy.")
                : (scan.Inside ? "Reported: halls are quiet." : "Reported: yard is quiet.");
            if (report.Proof.DoorLocked)
                line += " Door locked.";
            if (unhosted)
                line = "Reported: looks closed." + (report.Proof.DoorLocked ? " Door locked." : "");
            Session.SetAction(venue.Id, line);
            return line;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Report failed");
            var err = FriendlyReportError(ex);
            Session.SetAction(venue.Id, err);
            return err;
        }
    }

    private static string SendAction(string kind, bool inside, bool door)
    {
        if (door && !inside)
            return "door";
        if (kind == "happening")
            return inside ? "happening-in" : "happening-yard";
        return inside ? "wrapped-in" : "wrapped-yard";
    }

    public async Task<string> TryNote(VenueListing venue, string text)
    {
        if (!Configuration.AllowLogBook)
            return "Log book is off in Settings.";
        if (SendBlock() is { } blocked)
            return blocked;
        if (venue.Occupancy?.IsHappening != true)
            return "Log book is only for lanterns lit.";
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that listing first.";
        if (Session.OnPlot < TimeSpan.FromMinutes(Limits.LogBookDwellMinutes))
            return $"Stay about {Limits.LogBookDwellMinutes} minutes before leaving a note.";
        var noteWait = Session.NoteWait(venue.Id);
        if (noteWait > TimeSpan.Zero)
            return $"Already left a note here. Try again in {Math.Max(1, (int)Math.Ceiling(noteWait.TotalMinutes))}m.";
        var trimmed = (text ?? "").Trim();
        if (Copy.IsLogPhrase(trimmed) is false)
            return "Pick a line from the lists.";

        var here = HousingReader.Read();
        var scan = NearbyScan.Run(this);
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
                Apartment = here.Apartment,
                Subdivision = here.Subdivision,
                Inside = scan.Inside,
                ThresholdMet = scan.ThresholdMet,
            },
        };
        try
        {
            await occupancy.PostNote(Configuration.OccupancyApiUrl, post, CancellationToken.None).ConfigureAwait(true);
            Session.MarkNoted(venue.Id);
            await RefreshNotes(venue).ConfigureAwait(true);
            return "Note left in the log book.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Note failed");
            var msg = ex.Message ?? "";
            if (msg.Contains("already left a note", StringComparison.OrdinalIgnoreCase))
                return "Already left a note here this hour.";
            if (msg.Contains("lanterns are lit", StringComparison.OrdinalIgnoreCase))
                return "Log book is only for lanterns lit.";
            return "Note did not reach the server.";
        }
    }

    public async Task<string> TryOutdoor(OutdoorScan scan, bool? privateGathering)
    {
        if (!Configuration.NoteOutdoorScenes)
            return "Outdoor reports are off.";
        if (SendBlock() is { } blocked)
            return blocked;
        if (scan.Tier.Length == 0)
            return "Nothing to report.";

        var activity = Copy.OutdoorScene(Session.OutdoorSceneIdx);
        if (!Copy.IsOutdoorScene(activity))
            activity = "";

        var report = new OutdoorReport
        {
            Pocket = scan.Pocket,
            World = scan.World,
            Place = scan.Place,
            Zone = scan.Zone,
            Tier = scan.Tier,
            InCharacter = scan.InCharacter,
            Patrons = Math.Min(99, scan.Patrons),
            ZoneCount = Math.Min(99, scan.ZoneCount),
            Score = scan.Score,
            Voices = scan.Voices,
            Glance = scan.Glance,
            Emotes = scan.Emotes,
            Activity = activity,
            ReporterId = Configuration.ReporterId,
            PrivateGathering = privateGathering,
        };
        try
        {
            await occupancy.PostOutdoor(Configuration.OccupancyApiUrl, report, CancellationToken.None).ConfigureAwait(true);
            Session.LastOutdoorPost = DateTimeOffset.UtcNow;
            Session.LastOutdoorPocket = scan.Pocket;
            Session.LastOutdoorTier = scan.Tier;
            Session.OutdoorPrivate = null;
            Session.ClearWatch();
            Session.OutdoorLine = privateGathering == true
                ? "Marked private. It will stay off the list if others agree."
                : "Outdoor scene reported.";
            await RefreshOutdoors().ConfigureAwait(true);
            return Session.OutdoorLine;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Outdoor report failed");
            Session.OutdoorLine = "Outdoor report did not reach the server.";
            return Session.OutdoorLine;
        }
    }

    public async Task RefreshOutdoors()
    {
        if (!Configuration.OccupancyEnabled)
        {
            Outdoors = [];
            return;
        }
        try
        {
            Outdoors = await occupancy.GetOutdoors(Configuration.OccupancyApiUrl, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Outdoors refresh failed");
        }
    }

    public void StartOutdoorWatch()
    {
        if (Session.PlotKey.Length > 0)
        {
            Session.OutdoorLine = "Outdoor reports are for the street, not plots.";
            return;
        }
        var scan = NearbyScan.RunOutdoor(this);
        if (string.IsNullOrEmpty(scan.Pocket))
        {
            Session.OutdoorLine = "Not in a place LightsOn can report.";
            return;
        }
        var wait = Session.OutdoorAuditWait(scan.Pocket);
        if (wait > TimeSpan.Zero)
        {
            var secs = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
            Session.OutdoorLine = NearbyScan.NearbyPockets(Session.LastAuditPocket, scan.Pocket)
                ? $"Recently audited this area. Wait {secs}s."
                : $"Audit waits {secs}s.";
            return;
        }
        Session.PocketKey = scan.Pocket;
        Session.PocketSince = DateTimeOffset.UtcNow;
        Session.WatchPocket = scan.Pocket;
        Session.WatchSince = DateTimeOffset.UtcNow;
        Session.WatchPauseAt = default;
        Session.WatchPeak = scan;
        Session.WatchOrigin = scan.Origin;
        Session.WatchMapX = scan.MapX;
        Session.WatchMapY = scan.MapY;
        Session.WatchBusyHits = NearbyScan.TierRank(scan.Tier) >= 5 ? 1 : 0;
        Session.WatchReady = false;
        Session.LastAuditAt = DateTimeOffset.UtcNow;
        Session.LastAuditPocket = scan.Pocket;
        Session.OutdoorSceneIdx = 0;
        var start = scan.MapX > 0 ? $" ({scan.MapX:0.0}, {scan.MapY:0.0})" : "";
        Session.WatchLine = $"Stay here{start} · {Limits.OutdoorWatchSeconds}s";
        Session.OutdoorLine = "";
    }

    public void CancelOutdoorWatch()
    {
        Session.ClearWatch();
        Session.OutdoorLine = "Audit cancelled.";
    }

    public async Task FinishOutdoorWatch()
    {
        var peak = Session.WatchPeak;
        if (string.IsNullOrEmpty(peak.Pocket) || string.IsNullOrEmpty(peak.Tier))
        {
            Session.ClearWatch();
            Session.OutdoorLine = "Quiet here. Nothing to report.";
            return;
        }
        if (peak.AskPrivate)
        {
            Session.OutdoorPrivate = new OutdoorPending { Scan = peak };
            Session.ClearWatch();
            return;
        }
        Session.OutdoorLine = await TryOutdoor(peak, null).ConfigureAwait(true);
    }

    private static string FriendlyReportError(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("write limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("free tier", StringComparison.OrdinalIgnoreCase))
            return "Occupancy server is at today's write cap. Reports wait until midnight UTC.";
        if (msg.Contains("already reported", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("already sent", StringComparison.OrdinalIgnoreCase))
            return "This already went out. Wait a bit before sending again.";
        if (msg.Contains("too many", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("busy", StringComparison.OrdinalIgnoreCase))
            return "Server is catching up. Wait a few seconds.";
        if (msg.Contains("proof does not match", StringComparison.OrdinalIgnoreCase))
            return "Audit does not match the listed place. Nothing sent.";
        if (msg.Contains("thresholdMet", StringComparison.OrdinalIgnoreCase))
            return "Not enough company after the audit. Nothing sent.";
        if (msg.Contains("posted hours", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not open", StringComparison.OrdinalIgnoreCase))
            return "Directory does not show this listing as open right now. Nothing sent.";
        if (msg.Contains("unknown venue", StringComparison.OrdinalIgnoreCase))
            return "Listing is not on the occupancy server yet. Hit Refresh.";
        if (msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return "This build cannot write occupancy. Need a CI ingest key.";
        return string.IsNullOrWhiteSpace(msg) || msg.Length > 160
            ? "Report did not reach the server."
            : $"Report did not reach the server ({msg}).";
    }

    private string? SendBlock()
    {
        if (Configuration.ListingsOnly)
            return "Listings only is on. Nothing is sent.";
        if (!Configuration.ReportOptIn)
            return "Turn on Send reports in Settings first.";
        var left = Configuration.ReporterResetLockRemaining;
        if (left > TimeSpan.Zero)
        {
            var mins = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
            return $"Reporter id was reset. Reports wait {mins} more minute{(mins == 1 ? "" : "s")}.";
        }
        return null;
    }

    private void OnFramework(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if ((now - lastHere).TotalMilliseconds >= 500)
        {
            lastHere = now;
            Session.HereLine = Here.Line();
        }
        var tickSeconds = MainUiOpen || plotWindow.IsOpen || Session.Watching ? 2 : Limits.BackgroundTickSeconds;
        if ((now - lastTick).TotalSeconds < tickSeconds)
            return;
        lastTick = now;
        if (!ClientState.IsLoggedIn)
            return;

        TickPlot();
        TickOutdoorWatch();
        TickOutdoor();

        if ((now - lastPoll).TotalSeconds > 180)
            _ = RefreshVenues(false);
    }

    private void OnTerritory(uint _)
    {
        Session.PocketKey = "";
        Session.OutdoorPrivate = null;
        if (Session.Watching)
        {
            Session.ClearWatch();
            Session.WatchLine = "Left the area. Audit cancelled.";
        }
    }

    private void TickPlot()
    {
        var key = NearbyScan.PlotKey();
        if (key.Length == 0)
        {
            if (Session.PlotKey.Length > 0)
            {
                Session.ResetPlot("");
                if (Configuration.ClosePlotOnLeave)
                    plotWindow.IsOpen = false;
            }
            return;
        }

        if (key != Session.PlotKey)
        {
            var arrived = Session.PlotKey.Length == 0;
            Session.ResetPlot(key);
            Session.Hop = NearbyScan.ListedHere(Venues);
            if (Session.Hop is null)
            {
                if (Configuration.ClosePlotOnLeave)
                    plotWindow.IsOpen = false;
            }
            else if (arrived && Configuration.PromptOnEnter && Session.Hop.Resolution?.IsNow == true)
                plotWindow.IsOpen = true;
        }
        else
            Session.Hop ??= NearbyScan.ListedHere(Venues);

        var scan = NearbyScan.Run(this);
        LastScanLine = scan.OnPlot ? scan.Summary : Session.HereLine;
        if (scan.OnPlot)
            Session.Check.Absorb(scan);

        var venue = Session.Hop;
        if (venue is null)
            return;
        if (Session.Sending)
            return;
        if (!NearbyScan.OccupancyEligible(venue))
            return;
        if (SendBlock() is not null)
            return;
        if (!Configuration.AutoHappening || !scan.OnPlot || !scan.ThresholdMet)
            return;
        if (!scan.Inside && (HousingReader.DoorIsLocked() || Session.Check.DoorLocked))
            return;
        if (venue.Resolution?.IsNow != true)
            return;
        if (Session.ObserveSince == default)
            Session.ObserveSince = DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - Session.ObserveSince < TimeSpan.FromSeconds(Session.ObserveSeconds))
            return;

        var support = venue.Occupancy?.HappeningReports ?? 0;
        if (support >= Limits.AutoStopReports)
            return;
        var gap = support >= Limits.AutoSolidReports
            ? TimeSpan.FromMinutes(Limits.AutoSolidMinutes)
            : support >= 1
                ? TimeSpan.FromMinutes(Limits.AutoSomeMinutes)
                : TimeSpan.FromSeconds(Session.SendSeconds);
        if (!MainUiOpen && !plotWindow.IsOpen && support >= 1)
            gap = TimeSpan.FromMinutes(Math.Max(Limits.AutoSomeMinutes, 10));
        if (venue.Id == Session.LastAutoVenue && scan.Inside == Session.LastAutoInside
            && DateTimeOffset.UtcNow - Session.LastAutoHappening < gap)
            return;

        _ = AutoHappening(venue, scan.Inside, scan.Summary);
    }

    private async Task AutoHappening(VenueListing venue, bool inside, string chips)
    {
        var line = await TryReport(venue, "happening", true).ConfigureAwait(true);
        ActionLine = line;
        Session.SetAction(venue.Id, line);
        Session.LastAutoVenue = venue.Id;
        Session.LastAutoInside = inside;
        Session.LastAutoHappening = DateTimeOffset.UtcNow;
        Session.LastAutoChips = chips;
        if (line.StartsWith("Reported", StringComparison.Ordinal))
            Notify($"{venue.Name}: lanterns are lit.");
    }

    private void TickOutdoor()
    {
        if (Session.PlotKey.Length > 0)
            return;

        var scan = NearbyScan.RunOutdoor(this);
        if (string.IsNullOrEmpty(scan.Pocket))
            return;
        if (scan.Pocket != Session.PocketKey)
        {
            Session.PocketKey = scan.Pocket;
            Session.PocketSince = DateTimeOffset.UtcNow;
            Session.OutdoorPrivate = null;
            if (!Session.Watching)
                return;
        }

        if (Session.Watching)
            return;
        if (!Configuration.NoteOutdoorScenes || SendBlock() is not null)
            return;

        if (Session.InPocket < TimeSpan.FromMinutes(Limits.OutdoorDwellMinutes))
            return;
        if (Session.OutdoorWait(scan.Pocket, scan.Tier) > TimeSpan.Zero)
            return;
        if (DateTimeOffset.UtcNow - Session.LastOutdoorPost < TimeSpan.FromSeconds(Limits.SendRateSeconds))
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

    private void TickOutdoorWatch()
    {
        if (!Session.Watching)
            return;
        if (Session.PlotKey.Length > 0)
        {
            Session.ClearWatch();
            Session.WatchLine = "Outdoor reports are for the street.";
            return;
        }

        var player = ObjectTable.LocalPlayer;
        if (player is null)
            return;
        var dist = Vector3.Distance(player.Position, Session.WatchOrigin);
        var start = Session.WatchMapX > 0 ? $" ({Session.WatchMapX:0.0}, {Session.WatchMapY:0.0})" : "";
        if (dist > Limits.OutdoorCancelYalms)
        {
            Session.ClearWatch();
            Session.WatchLine = $"Moved too far from{start}. Audit cancelled.";
            Session.OutdoorLine = Session.WatchLine;
            return;
        }
        if (dist > Limits.OutdoorPauseYalms)
        {
            if (Session.WatchPauseAt == default)
                Session.WatchPauseAt = DateTimeOffset.UtcNow;
            Session.WatchReady = false;
            Session.WatchLine = $"Walk back to{start} · paused";
            return;
        }
        if (Session.WatchPauseAt != default)
        {
            Session.WatchSince += DateTimeOffset.UtcNow - Session.WatchPauseAt;
            Session.WatchPauseAt = default;
        }

        var scan = NearbyScan.RunOutdoor(this);
        if (string.IsNullOrEmpty(scan.Pocket))
        {
            Session.ClearWatch();
            Session.WatchLine = "Left the area. Audit cancelled.";
            Session.OutdoorLine = Session.WatchLine;
            return;
        }

        Session.PocketKey = scan.Pocket;
        if (NearbyScan.TierRank(scan.Tier) > NearbyScan.TierRank(Session.WatchPeak.Tier)
            || (scan.Patrons > Session.WatchPeak.Patrons && NearbyScan.TierRank(scan.Tier) >= NearbyScan.TierRank(Session.WatchPeak.Tier)))
            Session.WatchPeak = scan;
        if (NearbyScan.TierRank(scan.Tier) >= 5)
            Session.WatchBusyHits++;
        else
            Session.WatchBusyHits = 0;

        var elapsed = (DateTimeOffset.UtcNow - Session.WatchSince).TotalSeconds;
        var rank = NearbyScan.TierRank(Session.WatchPeak.Tier);
        var need = NearbyScan.WatchSeconds(Session.WatchPeak.Tier);
        if (rank >= 5 && Session.WatchBusyHits < 3)
            need = Limits.OutdoorWatchSomeSeconds;

        if (elapsed < need)
        {
            Session.WatchReady = false;
            Session.WatchLine = $"Stay here{start} · {Math.Max(1, (int)Math.Ceiling(need - elapsed))}s";
            return;
        }

        Session.WatchReady = true;
        Session.WatchLine = Session.WatchPeak.Tier.Length == 0
            ? "Quiet here. Nothing to report."
            : $"Looks {NearbyScan.TierLabel(Session.WatchPeak.Tier).ToLowerInvariant()}. Report this pocket?";
    }

    private void OnChat(IHandleableChatMessage message)
    {
        if (Session.PlotKey.Length == 0)
            return;

        var text = message.Message.TextValue;
        if (LooksLocked(text))
            Session.Check.MarkLocked();
        if (LooksMusic(text))
            Session.HeardMusic = true;

        var type = message.LogKind;
        var say = type == XivChatType.Say;
        var tell = type is XivChatType.TellIncoming or XivChatType.TellOutgoing;
        var party = type == XivChatType.Party;
        var emote = type is XivChatType.StandardEmote or XivChatType.CustomEmote;
        if (emote)
        {
            if (!Configuration.UseEmoteSignals)
                return;
            var emoteName = NearbyScan.NormName(message.Sender.TextValue);
            var meEmote = NearbyScan.NormName(ObjectTable.LocalPlayer?.Name.TextValue ?? "");
            if (emoteName.Length == 0)
                return;
            if (meEmote.Length == 0 || emoteName != meEmote)
                Session.HeardNames.Add(emoteName);
            Session.HeardEmote = true;
            return;
        }
        if (say && !Configuration.UseSaySignals)
            return;
        if ((tell || party) && !Configuration.UseChatSignals)
            return;
        if (!say && !tell && !party)
            return;

        var name = NearbyScan.NormName(message.Sender.TextValue);
        var me = NearbyScan.NormName(ObjectTable.LocalPlayer?.Name.TextValue ?? "");
        if (name.Length == 0)
            return;
        if (me.Length > 0 && name == me)
        {
            if (say || party)
                Session.SelfSpoke = true;
        }
        else
            Session.HeardNames.Add(name);
    }

    private static bool LooksMusic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.Contains("youtu", StringComparison.OrdinalIgnoreCase)
               || text.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLocked(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.Contains("house is locked", StringComparison.OrdinalIgnoreCase)
               || text.Contains("door is locked", StringComparison.OrdinalIgnoreCase)
               || text.Contains("cannot enter this house", StringComparison.OrdinalIgnoreCase);
    }

    private void OnCommand(string command, string args)
    {
        var key = (args ?? "").Trim().ToLowerInvariant();
        switch (key)
        {
            case "help":
                Notify("/lightson — open LightsOn");
                Notify("/lon here — current plot");
                Notify("/lon plot — current plot window");
                Notify("/lon config — settings");
                Notify("/lon refresh — reload listings");
                break;
            case "plot":
                TogglePlotWindow();
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
                Notify(Here.Line());
                break;
            }
            default:
                ToggleMainUi();
                break;
        }
    }
}
