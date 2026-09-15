using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace LightsOn;

internal static class Reach
{
    private static Dictionary<string, uint>? worldRegion;
    private static Dictionary<string, uint>? dcRegion;
    private static Dictionary<string, string>? worldDc;
    private static uint myRegion;
    private static string myWorld = "";

    public static bool CanVisitWorld(string? world)
    {
        Ensure();
        if (myRegion == 0 || string.IsNullOrWhiteSpace(world))
            return true;
        return worldRegion is not null
               && worldRegion.TryGetValue(world.Trim(), out var region)
               && region == myRegion;
    }

    public static bool CanVisitDc(string? dc)
    {
        Ensure();
        if (myRegion == 0 || string.IsNullOrWhiteSpace(dc))
            return true;
        return dcRegion is not null
               && dcRegion.TryGetValue(dc.Trim(), out var region)
               && region == myRegion;
    }

    public static string DataCenterOf(string? world)
    {
        Ensure();
        if (string.IsNullOrWhiteSpace(world) || worldDc is null)
            return "";
        return worldDc.TryGetValue(world.Trim(), out var dc) ? dc : "";
    }

    private static void Ensure()
    {
        var current = NearbyWorld();
        if (worldRegion is not null && current == myWorld)
            return;

        myWorld = current;
        myRegion = 0;
        var worlds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var dcs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<World>();
            foreach (var row in sheet)
            {
                var name = row.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var dc = row.DataCenter.ValueNullable;
                if (dc is null)
                    continue;
                var region = dc.Value.Region.RowId;
                if (region == 0)
                    continue;
                worlds[name] = region;
                var dcName = dc.Value.Name.ToString();
                if (dcName.Length > 0)
                {
                    dcs[dcName] = region;
                    names[name] = dcName;
                }
                if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                    myRegion = region;
            }
        }
        catch
        {
            worldRegion = worlds;
            dcRegion = dcs;
            worldDc = names;
            return;
        }

        worldRegion = worlds;
        dcRegion = dcs;
        worldDc = names;
    }

    private static string NearbyWorld()
    {
        try
        {
            var world = Plugin.PlayerState.CurrentWorld;
            return world.IsValid ? world.Value.Name.ToString() : "";
        }
        catch
        {
            return "";
        }
    }
}
