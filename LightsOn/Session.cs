using System;
using LightsOn.Api;

namespace LightsOn;

internal sealed class Session
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

    public TimeSpan OnPlot => PlotKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PlotSince;
    public TimeSpan InPocket => PocketKey.Length == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - PocketSince;
}

internal sealed class OutdoorPending
{
    public Scan.OutdoorScan Scan { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
