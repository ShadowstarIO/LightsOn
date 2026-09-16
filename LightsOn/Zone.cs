using System;
using Lumina.Excel.Sheets;

namespace LightsOn;

internal static class Zone
{
    public static (string Region, string Place, string Kind) Describe(uint territoryId)
    {
        if (territoryId is 0 or 0xFFFF)
            return ("", "", "");
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
            if (row is not TerritoryType t)
                return ("", "", "");
            var place = t.PlaceName.ValueNullable?.Name.ToString() ?? "";
            var region = t.PlaceNameZone.ValueNullable?.Name.ToString() ?? "";
            if (region.Length == 0 || string.Equals(region, place, StringComparison.OrdinalIgnoreCase))
                region = t.PlaceNameRegion.ValueNullable?.Name.ToString() ?? region;
            return (region.Trim(), place.Trim(), Kind(UseOf(t)));
        }
        catch
        {
            return ("", "", "");
        }
    }

    public static string Line(string world, string region, string place)
    {
        var bits = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(world))
            bits.Add(world.Trim());
        if (!string.IsNullOrWhiteSpace(region)
            && !string.Equals(region, place, StringComparison.OrdinalIgnoreCase))
            bits.Add(region.Trim());
        if (!string.IsNullOrWhiteSpace(place))
            bits.Add(place.Trim());
        return string.Join(" - ", bits);
    }

    public static string CountLabel(int n)
    {
        if (n >= 99)
            return "99+";
        return Math.Max(0, n).ToString();
    }

    private static uint UseOf(TerritoryType t)
    {
        try { return t.TerritoryIntendedUse.RowId; }
        catch { return 255; }
    }

    private static string Kind(uint use) => use switch
    {
        0 => "City · mounts · safe",
        1 => "Overworld · mounts · wildlife",
        2 => "Inn · no mounts · instance",
        3 or 4 => "Dungeon · no mounts · instance",
        8 => "Alliance raid · no mounts · instance",
        10 => "Trial · no mounts · instance",
        13 => "Housing · mounts · safe",
        14 => "Indoors · no mounts",
        16 or 17 or 36 => "Raid · no mounts · instance",
        18 or 28 or 37 or 39 => "PvP · no mounts · hostile",
        21 => "Firmament · mounts · safe",
        22 => "Wedding · mounts · safe",
        23 => "Gold Saucer · mounts · safe",
        26 or 41 or 47 or 48 or 52 or 53 or 61 => "Exploration · mounts · hostile",
        31 => "Deep dungeon · no mounts · instance",
        49 => "Island · mounts",
        60 => "Cosmic exploration · mounts",
        _ => "Adventure zone",
    };
}
