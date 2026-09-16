using System;
using System.Collections.Generic;
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
    public bool OnHouse => Ward is >= 1 and <= 30 && Plot is >= 1 and <= 60 && Apartment <= 0;
    public bool OnApartment => Ward is >= 1 and <= 30 && Apartment is >= 1 and <= 99;
    public bool OnProperty => OnHouse || OnApartment;
    public bool OnPlot => OnProperty;

    public string Summary => Place.Format(District, Ward, Plot, Apartment, Subdivision, Inside, compact: true);
    public string Long => Place.Format(District, Ward, Plot, Apartment, Subdivision, Inside, compact: false);
}

internal static class Place
{
    public static string Format(
        string district, int ward, int plot, int apartment, bool subdivision, bool? inside, bool compact)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(district))
            bits.Add(district.Trim());
        if (ward is >= 1 and <= 30)
            bits.Add(compact ? $"W{ward}" : $"Ward {ward}");
        if (apartment is >= 1 and <= 99)
        {
            if (subdivision)
                bits.Add(compact ? "Sub" : "Subdivision");
            bits.Add($"Apt {apartment}");
        }
        else if (plot is >= 1 and <= 60)
            bits.Add(compact ? $"P{plot}" : $"Plot {plot}");
        else if (subdivision && ward is >= 1 and <= 30)
            bits.Add(compact ? "Sub" : "Subdivision");
        if (inside is true)
            bits.Add(compact ? "In" : "inside");
        else if (inside is false && (plot is >= 1 and <= 60 || apartment is >= 1 and <= 99))
            bits.Add(compact ? "Out" : "yard");
        else if (inside is false && compact && ward is >= 1 and <= 30)
            bits.Add("Out");
        return string.Join(" ", bits);
    }
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
        var apartment = false;

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
                        if (hid.IsApartment)
                        {
                            apartment = true;
                            plot = 0;
                            if (hid.RoomNumber > 0)
                                room = hid.RoomNumber;
                            if (hid.WardIndex is >= 0 and < 30)
                                ward = hid.WardIndex + 1;
                            if (hid.ApartmentDivision == 1)
                                division = 2;
                            else if (hid.ApartmentDivision == 0)
                                division = 1;
                        }
                        else if (hid.PlotIndex is >= 0 and < 60)
                        {
                            plot = hid.PlotIndex + 1;
                            if (hid.WardIndex is >= 0 and < 30)
                                ward = hid.WardIndex + 1;
                        }
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
        if (apartment)
            return new HousingAddress(inside, district, ward, 0, room, subdivision);

        plot = CanonicalPlot(plot, subdivision);
        if (plot is < 1 or > 60)
            return new HousingAddress(inside, district, ward, 0, 0, subdivision);

        return new HousingAddress(inside, district, ward, plot, 0, subdivision);
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
