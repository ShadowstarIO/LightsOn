using System;
using System.Collections.Generic;
using System.Net.Http;
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

    public const string Version = "0.0.3.7";
    private const string CommandName = "/lightson";
    private const string CommandAlias = "/lon";

    private readonly HttpClient http;
    private readonly DirectoryClient directory;
    private readonly OccupancyClient occupancy;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
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
    public string LastScanLine { get; private set; } = "No scan yet.";
    public string ActionLine { get; set; } = "";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureReporterId();
        Configuration.Save();

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LightsOn/0.0.3.7 (+https://github.com/XozaShadow/LightsOn)");
        directory = new DirectoryClient(http);
        occupancy = new OccupancyClient(http);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "LightsOn. /lon here · /lon config",
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
    public void Notify(string text) => Chat.Print("[LightsOn] " + text);
    public bool CanSend => SendBlock() is null;

    public ScanResult ScanNow()
    {
        var result = NearbyScan.Run(this);
        LastScanLine = result.Summary;
        Session.LastScanAt = DateTimeOffset.UtcNow;
        if (result.OnPlot)
            Session.Check.Absorb(result);
        return result;
    }

    public void MarkDoorLocked()
    {
        Session.Check.MarkLocked();
        ActionLine = "Door marked locked on this plot.";
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
        if (!Configuration.OccupancyEnabled || venue.Occupancy?.IsHappening != true)
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
        if (SendBlock() is { } blocked)
            return blocked;
        if (!ClientState.IsLoggedIn || ObjectTable.LocalPlayer is null)
            return "Not logged in.";
        if (!NearbyScan.OccupancyEligible(venue))
            return Copy.NoPlot;
        if (!NearbyScan.MatchesVenue(venue))
            return "Go to that plot first. Reports are location-checked.";
        if (venue.Resolution?.IsNow != true)
            return "Only posted hours are reported. Nothing sent.";

        var scan = ScanNow();
        if (!scan.OnPlot)
            return scan.Summary;

        var requested = kind;
        var doorLocked = requested == "door_locked" || (!scan.Inside && Session.Check.DoorLocked);
        if (requested == "door_locked")
        {
            if (scan.Inside)
                return "Mark the door from the yard.";
            kind = scan.ThresholdMet ? "happening" : "wrapped_up";
            doorLocked = true;
        }

        if (kind != "happening" && kind != "wrapped_up")
            return "Unknown report kind.";

        var action = requested == "door_locked" ? "door" : SendAction(kind, scan.Inside, false);
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
            if (scan.ThresholdMet && requested != "door_locked")
                return "Enough company on this layer. Wrapped up is blocked.";
            if (!fromAuto && requested != "door_locked" && Session.WrapSureVenue != venue.Id)
            {
                Session.WrapSureVenue = venue.Id;
                return "Are you sure this layer is wrapped up? Press again to send.";
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
                Subdivision = here.Subdivision,
                Inside = scan.Inside,
                ThresholdMet = scan.ThresholdMet,
                DoorLocked = doorLocked && !scan.Inside,
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
            await RefreshVenues(true).ConfigureAwait(true);
            await RefreshLog(venue).ConfigureAwait(true);
            var line = kind == "happening" ? "Reported: lanterns are lit." : "Reported: wrapped up.";
            if (report.Proof.DoorLocked)
                line += " Door locked.";
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
            return "Go to that plot first.";
        if (Session.OnPlot < TimeSpan.FromMinutes(Limits.LogBookDwellMinutes))
            return $"Stay about {Limits.LogBookDwellMinutes} minutes before leaving a note.";
        var trimmed = (text ?? "").Trim();
        if (Array.IndexOf(Copy.LogPhrases, trimmed) < 0)
            return "Pick a line from the list.";

        var scan = ScanNow();
        var here = HousingReader.Read();
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
        if (!Configuration.NoteOutdoorScenes)
            return "Outdoor notes are off.";
        if (SendBlock() is { } blocked)
            return blocked;
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

    private static string FriendlyReportError(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("write limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("free tier", StringComparison.OrdinalIgnoreCase))
            return "Occupancy server is at today's write cap. Reports wait until midnight UTC.";
        if (msg.Contains("already reported", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("already sent", StringComparison.OrdinalIgnoreCase))
            return "This already went out. Wait a bit before sending again.";
        if (msg.Contains("proof does not match", StringComparison.OrdinalIgnoreCase))
            return "Scan does not match the listed plot. Nothing sent.";
        if (msg.Contains("thresholdMet", StringComparison.OrdinalIgnoreCase))
            return "Not enough company after the scan. Nothing sent.";
        if (msg.Contains("posted hours", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not open", StringComparison.OrdinalIgnoreCase))
            return "Only posted hours are reported. Nothing sent.";
        if (msg.Contains("unknown venue", StringComparison.OrdinalIgnoreCase))
            return "Listing is not on the occupancy server yet. Hit Refresh.";
        if (msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return "This build cannot write occupancy. Need a CI ingest key.";
        if (msg.Contains("busy", StringComparison.OrdinalIgnoreCase))
            return "Server is busy. Try again in a minute.";
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
            var here = HousingReader.Read();
            Session.HereLine = here.OnPlot ? here.Summary : "not on a plot";
        }
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
        Session.ResetPlot("");
        Session.OutdoorPrivate = null;
    }

    private void TickPlot()
    {
        var key = NearbyScan.PlotKey();
        if (key != Session.PlotKey)
        {
            Session.ResetPlot(key);
            Session.Hop = key.Length == 0 ? null : NearbyScan.ListedHere(Venues);
        }

        var scan = NearbyScan.Run(this);
        LastScanLine = scan.OnPlot ? scan.Summary : Session.HereLine;
        if (scan.OnPlot)
            Session.Check.Absorb(scan);

        var venue = Session.Hop;
        if (venue is null)
            return;
        if (!NearbyScan.OccupancyEligible(venue))
            return;
        if (SendBlock() is not null)
            return;
        if (!Configuration.AutoHappening || !scan.OnPlot || !scan.ThresholdMet)
            return;
        if (venue.Resolution?.IsNow != true)
            return;
        if (Session.ObserveSince == default)
            Session.ObserveSince = DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - Session.ObserveSince < TimeSpan.FromSeconds(Limits.ObserveSeconds))
            return;
        if (venue.Id == Session.LastAutoVenue && scan.Inside == Session.LastAutoInside
            && DateTimeOffset.UtcNow - Session.LastAutoHappening < TimeSpan.FromSeconds(Limits.SendRateSeconds))
            return;

        _ = AutoHappening(venue, scan.Inside);
    }

    private async Task AutoHappening(VenueListing venue, bool inside)
    {
        var line = await TryReport(venue, "happening", true).ConfigureAwait(true);
        ActionLine = line;
        Session.SetAction(venue.Id, line);
        if (line.StartsWith("Reported", StringComparison.Ordinal))
        {
            Session.LastAutoVenue = venue.Id;
            Session.LastAutoInside = inside;
            Session.LastAutoHappening = DateTimeOffset.UtcNow;
            Notify($"{venue.Name}: lanterns are lit.");
        }
    }

    private void TickOutdoor()
    {
        if (!Configuration.NoteOutdoorScenes || SendBlock() is not null)
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

        if (Session.InPocket < TimeSpan.FromMinutes(Limits.OutdoorDwellMinutes))
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
                Notify("/lon config — settings");
                Notify("/lon refresh — reload listings");
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
                var here = HousingReader.Read();
                Notify($"{NearbyScan.CurrentWorldName()} · {here.Summary}");
                break;
            }
            default:
                ToggleMainUi();
                break;
        }
    }
}
