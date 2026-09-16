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
    public bool Hiring { get; set; }
    public string? Website { get; set; }
    public string? Discord { get; set; }
    public List<string> Description { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public VenueResolution? Resolution { get; set; }
    public List<VenueSchedule> Schedule { get; set; } = [];

    [JsonIgnore] public OccupancySnapshot Occupancy { get; set; } = OccupancySnapshot.Unknown;
    [JsonIgnore] public IReadOnlyList<GuestNote> Notes { get; set; } = [];
    [JsonIgnore] public IReadOnlyList<OccupancyEvent> Log { get; set; } = [];

    public string DescriptionText
    {
        get
        {
            if (Description is not { Count: > 0 })
                return "";
            return string.Join("\n", Description);
        }
    }

    public void BindHours()
    {
        var open = Schedule.Find(s => s.Resolution?.IsNow == true)
                   ?? Schedule.Find(s => s.Resolution?.IsWithinWeek == true);
        if (open?.Resolution is not null)
            Resolution = open.Resolution;
    }

    public string HoursLine
    {
        get
        {
            var r = Resolution;
            if (r is null)
                return "Hours not listed";
            if (r.IsNow)
            {
                if (r.End is DateTimeOffset end)
                    return $"Open now · until {end.ToLocalTime():h:mm tt}";
                return "Open now";
            }
            if (r.Start is DateTimeOffset start)
                return $"Next {start.ToLocalTime():ddd h:mm tt}";
            return "Not in posted hours";
        }
    }
}

public sealed class VenueSchedule
{
    public VenueResolution? Resolution { get; set; }
}

public sealed class VenueLocation
{
    public string DataCenter { get; set; } = "";
    public string World { get; set; } = "";
    public string District { get; set; } = "";
    public int Ward { get; set; }
    public int Plot { get; set; }
    public int Apartment { get; set; }
    public int Room { get; set; }
    public bool Subdivision { get; set; }

    public int RoomNo => Apartment > 0 ? Apartment : Room;
    public bool IsApartment => Plot <= 0 && RoomNo > 0;

    public int HousePlot => IsApartment ? 0 : HousingReader.CanonicalPlot(Plot, Subdivision && RoomNo == 0);

    public string Address => Head + Place.Format(District, Ward, HousePlot, IsApartment ? RoomNo : 0,
        IsApartment && Subdivision, null, compact: true);

    public string AddressLong => Head + Place.Format(District, Ward, HousePlot, IsApartment ? RoomNo : 0,
        IsApartment && Subdivision, null, compact: false);

    private string Head
    {
        get
        {
            var dc = string.IsNullOrWhiteSpace(DataCenter) ? "" : DataCenter.Trim() + " · ";
            var world = string.IsNullOrWhiteSpace(World) ? "" : World.Trim() + " · ";
            return dc + world;
        }
    }
}

public sealed class VenueResolution
{
    public bool IsNow { get; set; }
    public bool IsWithinWeek { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
}

public sealed class OccupancySnapshot
{
    public static OccupancySnapshot Unknown { get; } = new();

    public string VenueId { get; set; } = "";
    public string State { get; set; } = "unknown";
    public int HappeningReports { get; set; }
    public int WrappedUpReports { get; set; }
    public int InteriorHappening { get; set; }
    public int InteriorWrapped { get; set; }
    public int ExteriorHappening { get; set; }
    public int ExteriorWrapped { get; set; }
    public bool DoorLocked { get; set; }
    public bool BothLayers { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public float? Lean { get; set; }

    [JsonIgnore] public bool IsHappening => State is "happening" or "mixed";
    [JsonIgnore] public bool IsWrappedUp => State == "wrapped_up";
    [JsonIgnore] public bool IsMixed => State == "mixed";

    [JsonIgnore]
    public float LeanValue =>
        Lean ?? InteriorHappening * 2 + ExteriorHappening - InteriorWrapped * 2 - ExteriorWrapped;
}

public sealed class OccupancyEvent
{
    public string Kind { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public bool Inside { get; set; }
    public bool ThresholdMet { get; set; }
    public bool DoorLocked { get; set; }
    public bool Voices { get; set; }
    public bool Glance { get; set; }
    public bool Music { get; set; }
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
    public int Apartment { get; set; }
    public bool Subdivision { get; set; }
    public bool Inside { get; set; }
    public bool ThresholdMet { get; set; }
    public bool DoorLocked { get; set; }
    public bool Unhosted { get; set; }
    public bool Voices { get; set; }
    public bool Glance { get; set; }
    public bool Music { get; set; }
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
