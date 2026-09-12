using System;
using FFXIVClientStructs.FFXIV.Client.Game;

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
    public static HousingAddress Read(string territoryName)
    {
        var district = ResolveDistrict(territoryName);
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
                    ward = h->GetCurrentWard() + 1;
                    plot = h->GetCurrentPlot() + 1;
                    room = h->GetCurrentRoom();
                    division = h->GetCurrentDivision();
                    inside = h->IsInside();
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
            return new HousingAddress(false, district, ward, 0, room, subdivision);

        return new HousingAddress(inside, district, ward, plot, room, subdivision);
    }

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
        return territoryName;
    }

    private static bool Contains(string hay, string needle) =>
        hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
