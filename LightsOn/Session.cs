using System;
using System.Collections.Generic;
using LightsOn.Api;
using LightsOn.Scan;

namespace LightsOn;

public sealed class Session
{
    public string PlotKey { get; set; } = "";
    public DateTimeOffset PlotSince { get; set; }
    public string PocketKey { get; set; } = "";
    public DateTimeOffset PocketSince { get; set; }
    public VenueListing? Hop { get; set; }
    public bool HopDismissed { get; set; }
    public string? WrapSureVenue { get; set; }
    public string LastAutoChips { get; set; } = "";
    public string LastAutoVenue { get; set; } = "";
    public bool LastAutoInside { get; set; }
    public DateTimeOffset LastAutoHappening { get; set; }
    public DateTimeOffset LastOutdoorPost { get; set; }
    public string LastOutdoorPocket { get; set; } = "";
    public string LastOutdoorTier { get; set; } = "";
    public DateTimeOffset ObserveSince { get; set; }
    public DateTimeOffset LastScanAt { get; set; }
    public OutdoorPending? OutdoorPrivate { get; set; }
    public PlotCheck Check { get; } = new();
    public HashSet<string> HeardNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SelfSpoke { get; set; }
    public bool HeardMusic { get; set; }
    public bool HeardEmote { get; set; }
    public string HereLine { get; set; } = "Not logged in.";
    public Dictionary<string, string> ActionByVenue { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> Sent { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> Noted { get; } = new(StringComparer.Ordinal);
    public bool Sending { get; set; }
    public int SendSeconds { get; set; } = Limits.SendRateSeconds;
    public int ScanSeconds { get; set; } = Limits.ScanCooldownSeconds;
    public int ObserveSeconds { get; set; } = Limits.ObserveSeconds;
    public int TourCount { get; set; }

    public string WatchPocket { get; set; } = "";
    public DateTimeOffset WatchSince { get; set; }
    public OutdoorScan WatchPeak { get; set; }
    public int WatchBusyHits { get; set; }
    public bool WatchReady { get; set; }
    public string WatchLine { get; set; } = "";
    public string OutdoorLine { get; set; } = "";

    public bool Watching => WatchPocket.Length > 0;
    public bool Touring => TourCount >= Limits.TourVenues;
    public TimeSpan OnPlot => PlotKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PlotSince;
    public TimeSpan InPocket => PocketKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PocketSince;

    public string ActionFor(string? venueId)
    {
        if (string.IsNullOrEmpty(venueId))
            return "";
        return ActionByVenue.TryGetValue(venueId, out var line) ? line : "";
    }

    public void SetAction(string venueId, string line) => ActionByVenue[venueId] = line;

    public TimeSpan SendWait(string venueId, string action)
    {
        if (!Sent.TryGetValue($"{venueId}:{action}", out var at))
            return TimeSpan.Zero;
        var left = TimeSpan.FromSeconds(SendSeconds) - (DateTimeOffset.UtcNow - at);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public void MarkSent(string venueId, string action) =>
        Sent[$"{venueId}:{action}"] = DateTimeOffset.UtcNow;

    public TimeSpan NoteWait(string venueId)
    {
        if (!Noted.TryGetValue(venueId, out var at))
            return TimeSpan.Zero;
        var left = TimeSpan.FromMinutes(Limits.LogBookNoteMinutes) - (DateTimeOffset.UtcNow - at);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public void MarkNoted(string venueId) => Noted[venueId] = DateTimeOffset.UtcNow;

    public TimeSpan OutdoorWait(string pocket, string newTier)
    {
        if (LastOutdoorPocket.Length == 0 || LastOutdoorPocket != pocket || LastOutdoorPost == default)
            return TimeSpan.Zero;
        var elapsed = DateTimeOffset.UtcNow - LastOutdoorPost;
        var lastRank = NearbyScan.TierRank(LastOutdoorTier);
        var nextRank = NearbyScan.TierRank(newTier);
        var need = nextRank > lastRank
            ? TimeSpan.FromMinutes(Limits.OutdoorUpgradeMinutes)
            : TimeSpan.FromMinutes(NearbyScan.LockMinutes(LastOutdoorTier));
        var left = need - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public TimeSpan ScanWait
    {
        get
        {
            if (LastScanAt == default)
                return TimeSpan.Zero;
            var left = TimeSpan.FromSeconds(ScanSeconds) - (DateTimeOffset.UtcNow - LastScanAt);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void ClearWatch()
    {
        WatchPocket = "";
        WatchSince = default;
        WatchPeak = default;
        WatchBusyHits = 0;
        WatchReady = false;
        WatchLine = "";
    }

    public void ResetPlot(string key)
    {
        PlotKey = key;
        PlotSince = DateTimeOffset.UtcNow;
        HopDismissed = false;
        WrapSureVenue = null;
        ObserveSince = key.Length == 0 ? default : DateTimeOffset.UtcNow;
        LastAutoChips = "";
        LastAutoVenue = "";
        LastAutoInside = false;
        LastAutoHappening = default;
        Check.Clear();
        HeardNames.Clear();
        SelfSpoke = false;
        HeardMusic = false;
        HeardEmote = false;
        Hop = null;
        if (key.Length > 0)
            ClearWatch();
    }
}

public sealed class PlotCheck
{
    public ScanResult? Yard { get; set; }
    public ScanResult? Inside { get; set; }
    public bool DoorLocked { get; set; }

    public bool HasYard => Yard is { OnPlot: true };
    public bool HasInside => Inside is { OnPlot: true };

    public void Absorb(ScanResult scan)
    {
        if (!scan.OnPlot)
            return;
        if (scan.Inside)
        {
            Inside = scan;
            DoorLocked = false;
        }
        else
            Yard = scan;
    }

    public void MarkLocked()
    {
        if (HasInside)
            return;
        DoorLocked = true;
    }

    public void Clear()
    {
        Yard = null;
        Inside = null;
        DoorLocked = false;
    }
}

public sealed class OutdoorPending
{
    public OutdoorScan Scan { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
