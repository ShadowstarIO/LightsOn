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
        ? $"{District}  W{Ward}{(Subdivision ? " sub" : "")}  P{Plot}" + (Apartment > 0 ? $"  R{Apartment}" : "") + (Inside ? "  inside" : "  yard")
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

        var subdivision = division == 2 || plot is >= 31 and <= 60;
        if (plot is < 1 or > 60)
            return new HousingAddress(inside, district, ward, 0, room, subdivision);

        return new HousingAddress(inside, district, ward, plot, room, subdivision);
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
        name is "Mist" or "The Lavender Beds" or "The Goblet" or "Shirogane" or "Empyreum";

    public static string ResolveDistrict(string territoryName)
    {
        if (string.IsNullOrWhiteSpace(territoryName))
            return string.Empty;
        if (Contains(territoryName, "Mist") || Contains(territoryName, "Topmast"))
            return "Mist";
        if (Contains(territoryName, "Lavender") || Contains(territoryName, "Lily Hills"))
            return "The Lavender Beds";
        if (Contains(territoryName, "Goblet") || Contains(territoryName, "Sultana"))
            return "The Goblet";
        if (Contains(territoryName, "Shirogane") || Contains(territoryName, "Kobai"))
            return "Shirogane";
        if (Contains(territoryName, "Empyreum") || Contains(territoryName, "Ingleside"))
            return "Empyreum";
        return "";
    }

    private static bool Contains(string hay, string needle) =>
        hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
