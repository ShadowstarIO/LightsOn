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
    public bool WrappedConfirm { get; set; }
    public DateTimeOffset LastAutoHappening { get; set; }
    public string LastAutoVenue { get; set; } = "";
    public DateTimeOffset LastOutdoorPost { get; set; }
    public OutdoorPending? OutdoorPrivate { get; set; }
    public PlotCheck Check { get; } = new();
    public HashSet<string> HeardNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SelfSpoke { get; set; }

    public TimeSpan OnPlot => PlotKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PlotSince;
    public TimeSpan InPocket => PocketKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PocketSince;

    public void ResetPlot(string key)
    {
        PlotKey = key;
        PlotSince = DateTimeOffset.UtcNow;
        HopDismissed = false;
        WrappedConfirm = false;
        Check.Clear();
        HeardNames.Clear();
        SelfSpoke = false;
        Hop = null;
    }
}

public sealed class PlotCheck
{
    public ScanResult? Yard { get; set; }
    public ScanResult? Inside { get; set; }
    public bool DoorLocked { get; set; }

    public bool HasYard => Yard is { OnPlot: true };
    public bool HasInside => Inside is { OnPlot: true };
    public bool Ready => HasYard && (DoorLocked || HasInside);
    public bool Enough => Yard is { ThresholdMet: true } || Inside is { ThresholdMet: true };

    public string Guide
    {
        get
        {
            if (!HasYard && !HasInside)
                return "Stand in the yard or step inside so LightsOn can scan this layer.";
            if (!HasYard)
                return "Yard not scanned yet. Step outside for a moment.";
            if (DoorLocked)
                return Enough
                    ? "Door locked · yard has company."
                    : "Door locked · yard is quiet.";
            if (!HasInside)
                return "Yard is done. Go inside, or mark the door locked. The street cannot see the room.";
            return Enough
                ? "Both layers checked · enough company."
                : "Both layers checked · quiet.";
        }
    }

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
        {
            Yard = scan;
        }
    }

    public void MarkLocked()
    {
        if (HasYard && !HasInside)
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
