using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace LightsOn;

public readonly record struct HousingAddress(
    bool Inside,
    string District,
    int Ward,
    int Plot,
    int Apartment,
    bool Subdivision)
{
    public bool OnPlot => Ward is >= 1 and <= 30 && Plot is >= 1 and <= 60;

    public string Summary => OnPlot
        ? $"{District}  W{Ward}  P{Plot}" + (Apartment > 0 ? $"{(Subdivision ? " sub" : "")} R{Apartment}" : "") + (Inside ? "  inside" : "  yard")
        : "not on a plot";
}

internal static class HousingReader
{
    public static HousingAddress Read()
    {
        var district = DistrictFromTerritory((ushort)Plugin.ClientState.TerritoryType);
        var ward = 0;
        var plot = 0;
        var room = 0;
        var division = 1;
        var inside = false;

        try
        {
            unsafe
            {
                var h = HousingManager.Instance();
                if (h != null)
                {
                    inside = h->IsInside();
                    ward = h->GetCurrentWard() + 1;
                    var rawPlot = h->GetCurrentPlot();
                    if (rawPlot is >= 0 and < 60)
                        plot = rawPlot + 1;
                    room = h->GetCurrentRoom();
                    division = h->GetCurrentDivision();

                    if (inside)
                    {
                        var hid = h->GetCurrentIndoorHouseId();
                        if (!hid.IsApartment && hid.PlotIndex < 60)
                        {
                            plot = hid.PlotIndex + 1;
                            ward = hid.WardIndex + 1;
                        }
                        if (hid.IsApartment)
                            room = hid.RoomNumber > 0 ? hid.RoomNumber : room;
                        var fromHouse = DistrictFromTerritory(hid.TerritoryTypeId);
                        if (IsKnownDistrict(fromHouse))
                            district = fromHouse;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "HousingManager read failed");
        }

        if (ward is < 1 or > 30)
            return default;

        var subdivision = division == 2;
        plot = CanonicalPlot(plot, subdivision);
        if (plot is < 1 or > 60)
            return new HousingAddress(inside, district, ward, 0, room, subdivision);

        return new HousingAddress(inside, district, ward, plot, room, subdivision);
    }

    public static int CanonicalPlot(int plot, bool subdivision)
    {
        if (plot is >= 1 and <= 30 && subdivision)
            return plot + 30;
        return plot;
    }

    public static bool SameDistrict(string? a, string? b)
    {
        var ra = ResolveDistrict(a ?? "");
        var rb = ResolveDistrict(b ?? "");
        if (ra.Length > 0 && rb.Length > 0)
            return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string DistrictFromTerritory(ushort territoryId)
    {
        if (territoryId is 0 or 0xFFFF)
            return "";
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
            if (row is not TerritoryType t)
                return "";
            var place = t.PlaceName.ValueNullable?.Name.ToString() ?? "";
            var zone = t.PlaceNameZone.ValueNullable?.Name.ToString() ?? "";
            var resolved = ResolveDistrict(place);
            if (IsKnownDistrict(resolved))
                return resolved;
            resolved = ResolveDistrict(zone);
            return IsKnownDistrict(resolved) ? resolved : "";
        }
        catch
        {
            return "";
        }
    }

    public static bool IsKnownDistrict(string name) =>
        name is "Mist" or "Lavender Beds" or "Goblet" or "Shirogane" or "Empyreum";

    public static string ResolveDistrict(string territoryName)
    {
        if (string.IsNullOrWhiteSpace(territoryName))
            return string.Empty;
        if (Contains(territoryName, "Mist") || Contains(territoryName, "Topmast"))
            return "Mist";
        if (Contains(territoryName, "Lavender") || Contains(territoryName, "Lily Hills"))
            return "Lavender Beds";
        if (Contains(territoryName, "Goblet") || Contains(territoryName, "Sultana"))
            return "Goblet";
        if (Contains(territoryName, "Shirogane") || Contains(territoryName, "Kobai"))
            return "Shirogane";
        if (Contains(territoryName, "Empyreum") || Contains(territoryName, "Ingleside"))
            return "Empyreum";
        return "";
    }

    private static bool Contains(string hay, string needle) =>
        hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static bool DoorIsLocked()
    {
        try
        {
            unsafe
            {
                var h = HousingManager.Instance();
                if (h == null || h->OutdoorTerritory == null)
                    return false;
                var plot = h->GetCurrentPlot();
                if (plot is < 0 or >= 60)
                    return false;
                return !h->OutdoorTerritory->Plots[plot].IsOpen;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Plot open flag failed");
            return false;
        }
    }
}
