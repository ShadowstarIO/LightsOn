using System;
using System.Collections.Generic;
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

    private static readonly Dictionary<string, uint> Territories = new(StringComparer.OrdinalIgnoreCase);

    public static uint FindTerritory(string place)
    {
        var name = (place ?? "").Trim();
        if (name.Length == 0)
            return 0;
        if (Territories.TryGetValue(name, out var cached))
            return cached;
        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<TerritoryType>())
            {
                var label = row.PlaceName.ValueNullable?.Name.ToString() ?? "";
                if (!string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                var use = UseOf(row);
                if (use is not (0 or 1 or 13 or 21 or 23 or 41))
                    continue;
                Territories[name] = row.RowId;
                return row.RowId;
            }
        }
        catch
        {
            return 0;
        }
        Territories[name] = 0;
        return 0;
    }

    public static string Line(string world, string region, string place)
    {
        var bits = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(world))
            bits.Add(world.Trim());
        var p = (place ?? "").Trim();
        var r = (region ?? "").Trim();
        if (r.Length > 0 && (p.Length == 0 || p.IndexOf(r, StringComparison.OrdinalIgnoreCase) < 0))
            bits.Add(r);
        if (p.Length > 0)
            bits.Add(p);
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
        0 => "City · no mounts · safe",
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
        22 => "Wedding · no mounts · safe",
        23 => "Gold Saucer · no mounts · safe",
        26 or 41 or 47 or 48 or 52 or 53 or 61 => "Exploration · mounts · hostile",
        31 => "Deep dungeon · no mounts · instance",
        49 => "Island · mounts",
        60 => "Cosmic exploration · mounts",
        _ => "Adventure zone",
    };
}
