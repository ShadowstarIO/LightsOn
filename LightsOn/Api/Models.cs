using System;
using System.Collections.Generic;
using System.Linq;
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
    public List<VenueOverride> ScheduleOverrides { get; set; } = [];

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
        if (Resolution?.IsNow == true)
            return;
        var ov = ScheduleOverrides.Find(o => o.Open && o.IsNow);
        if (ov is not null)
        {
            Resolution = new VenueResolution
            {
                IsNow = true,
                IsWithinWeek = true,
                Start = ov.Start,
                End = ov.End,
            };
            return;
        }
        var open = Schedule.Find(s => s.Resolution?.IsNow == true)
                   ?? Schedule.Find(s => s.Resolution?.IsWithinWeek == true);
        if (open?.Resolution is not null)
            Resolution = open.Resolution;
    }

    public string HoursLine
    {
        get
        {
            var upcoming = UpcomingHours().Take(3).ToList();
            if (upcoming.Count == 0)
                return "Hours not listed";
            if (upcoming[0].IsNow)
            {
                var until = upcoming[0].End is DateTimeOffset end
                    ? $" until {end.ToLocalTime():h:mm tt}"
                    : "";
                var later = upcoming.Skip(1).Select(FormatSlot).Where(s => s.Length > 0).ToList();
                return later.Count == 0
                    ? $"Open now{until}"
                    : $"Open now{until} · Next {string.Join(" · ", later)}";
            }
            return "Next " + string.Join(" · ", upcoming.Select(FormatSlot).Where(s => s.Length > 0));
        }
    }

    public IEnumerable<VenueResolution> UpcomingHours()
    {
        var rows = new List<VenueResolution>();
        if (Resolution is not null)
            rows.Add(Resolution);
        foreach (var ov in ScheduleOverrides)
        {
            if (!ov.Open)
                continue;
            rows.Add(new VenueResolution
            {
                IsNow = ov.IsNow,
                IsWithinWeek = true,
                Start = ov.Start,
                End = ov.End,
            });
        }
        foreach (var slot in Schedule)
        {
            if (slot.Resolution is not null)
                rows.Add(slot.Resolution);
        }

        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        return rows
            .Where(r => r.IsNow || (r.End ?? r.Start) > now)
            .GroupBy(r => r.Start?.ToUnixTimeSeconds() ?? 0)
            .Select(g => g.First())
            .OrderByDescending(r => r.IsNow)
            .ThenBy(r => r.Start ?? DateTimeOffset.MaxValue);
    }

    private static string FormatSlot(VenueResolution r)
    {
        if (r.Start is DateTimeOffset start && r.End is DateTimeOffset end)
            return $"{start.ToLocalTime():ddd h:mm tt}–{end.ToLocalTime():h:mm tt}";
        if (r.Start is DateTimeOffset only)
            return only.ToLocalTime().ToString("ddd h:mm tt");
        return "";
    }
}

public sealed class VenueSchedule
{
    public VenueResolution? Resolution { get; set; }
}

public sealed class VenueOverride
{
    public bool Open { get; set; }
    public bool IsNow { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
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

    public string AddressPanel
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(DataCenter))
                bits.Add(DataCenter.Trim());
            if (!string.IsNullOrWhiteSpace(World))
                bits.Add(World.Trim());
            if (!string.IsNullOrWhiteSpace(District))
                bits.Add(District.Trim());
            if (Ward is >= 1 and <= 30)
                bits.Add($"Ward {Ward}");
            if (Subdivision)
                bits.Add("Sub");
            if (IsApartment)
            {
                bits.Add($"Apt {RoomNo}");
                if (Room > 0 && Room != RoomNo)
                    bits.Add($"Room {Room}");
            }
            else
            {
                var plot = Plot is >= 31 and <= 60 ? Plot - 30 : Plot;
                if (plot is >= 1 and <= 60)
                    bits.Add($"Plot {plot}");
            }
            return string.Join(" - ", bits);
        }
    }

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
