using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LightsOn.Api;

public sealed class VenueListing
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public VenueLocation? Location { get; set; }
    public bool Sfw { get; set; } = true;
    public VenueResolution? Resolution { get; set; }

    [JsonIgnore] public OccupancySnapshot Occupancy { get; set; } = OccupancySnapshot.Unknown;
    [JsonIgnore] public IReadOnlyList<GuestNote> Notes { get; set; } = [];
}

public sealed class VenueLocation
{
    public string DataCenter { get; set; } = "";
    public string World { get; set; } = "";
    public string District { get; set; } = "";
    public int Ward { get; set; }
    public int Plot { get; set; }
    public int Apartment { get; set; }
    public bool Subdivision { get; set; }

    public string Address
    {
        get
        {
            var plot = Plot > 0 ? $"Plot {Plot}" : "";
            var sub = Subdivision ? " (Sub)" : "";
            var apt = Apartment > 0 ? $" Apt {Apartment}" : "";
            return $"{World} · {District} Ward {Ward} {plot}{sub}{apt}".Trim();
        }
    }
}

public sealed class VenueResolution
{
    public bool IsNow { get; set; }
}

public sealed class OccupancySnapshot
{
    public static OccupancySnapshot Unknown { get; } = new();

    public string VenueId { get; set; } = "";
    public string State { get; set; } = "unknown";
    public int HappeningReports { get; set; }
    public int WrappedUpReports { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore] public bool IsHappening => State == "happening";
    [JsonIgnore] public bool IsWrappedUp => State == "wrapped_up";
}

public sealed class OccupancyReport
{
    public string VenueId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ReporterId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public OccupancyProof Proof { get; set; } = new();
}

public sealed class OccupancyProof
{
    public string World { get; set; } = "";
    public string District { get; set; } = "";
    public int Ward { get; set; }
    public int Plot { get; set; }
    public bool Subdivision { get; set; }
    public bool Inside { get; set; }
    public bool ThresholdMet { get; set; }
}

public sealed class GuestNote
{
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class NotePost
{
    public string VenueId { get; set; } = "";
    public string ReporterId { get; set; } = "";
    public string Text { get; set; } = "";
    public OccupancyProof Proof { get; set; } = new();
}

public sealed class OutdoorSnapshot
{
    public string Pocket { get; set; } = "";
    public string World { get; set; } = "";
    public string Place { get; set; } = "";
    public string Tier { get; set; } = "";
    public bool InCharacter { get; set; }
    public int Reports { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class OutdoorReport
{
    public string Pocket { get; set; } = "";
    public string World { get; set; } = "";
    public string Place { get; set; } = "";
    public string Tier { get; set; } = "";
    public bool InCharacter { get; set; }
    public string ReporterId { get; set; } = "";
    public bool? PrivateGathering { get; set; }
}
