using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LightsOn.Scan;
using Lumina.Excel.Sheets;

namespace LightsOn;

internal static class Here
{
    public static string Line()
    {
        if (!Plugin.ClientState.IsLoggedIn)
            return "Not logged in.";

        var dc = DataCenter();
        var world = NearbyScan.CurrentWorldName();
        var head = Join(dc, world);
        var people = $" {SeIconChar.CrossWorld.ToIconString()}{PeopleInRange()}";

        var housing = HousingReader.Read();
        if (housing.Ward is >= 1 and <= 30)
        {
            var place = housing.Summary;
            var (mx, my) = MapCoords();
            if (!housing.OnProperty && mx > 0 && my > 0)
                place += $" ({mx:0.0}, {my:0.0})";
            return Join(head, place) + people;
        }

        var zone = NearbyScan.CurrentZoneName();
        var (zx, zy) = MapCoords();
        var coords = zx > 0 && zy > 0 ? $" ({zx:0.0}, {zy:0.0})" : "";
        return Join(head, zone + coords) + people;
    }

    public static int PeopleInRange()
    {
        try
        {
            var self = Plugin.ObjectTable.LocalPlayer;
            var n = 0;
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj is null || obj.ObjectKind != ObjectKind.Pc || obj is not IPlayerCharacter pc)
                    continue;
                if (self is not null && pc.EntityId == self.EntityId)
                    continue;
                n++;
            }
            return n;
        }
        catch
        {
            return 0;
        }
    }

    public static (float X, float Y) MapCoords()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return (0, 0);
        return WorldToMap(Plugin.ClientState.TerritoryType, player.Position.X, player.Position.Z);
    }

    public static (float X, float Y) WorldToMap(uint territoryId, float worldX, float worldZ)
    {
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
            if (row is not TerritoryType t)
                return (0, 0);
            var map = t.Map.ValueNullable;
            if (map is null)
                return (0, 0);
            return (
                ToMap(worldX, map.Value.OffsetX, map.Value.SizeFactor),
                ToMap(worldZ, map.Value.OffsetY, map.Value.SizeFactor));
        }
        catch
        {
            return (0, 0);
        }
    }

    public static bool FlagPocket(string pocket)
    {
        if (!NearbyScan.TryParsePocket(pocket, out _, out var territory, out var gx, out var gz))
            return false;
        var worldX = (gx + 0.5f) * 20f;
        var worldZ = (gz + 0.5f) * 20f;
        return Flag(territory, worldX, worldZ);
    }

    public static bool Flag(uint territoryId, float worldX, float worldZ)
    {
        try
        {
            unsafe
            {
                var agent = AgentMap.Instance();
                if (agent == null)
                    return false;
                var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
                if (row is not TerritoryType t)
                    return false;
                var mapId = t.Map.RowId;
                agent->SetFlagMapMarker(territoryId, mapId, worldX, worldZ);
                agent->OpenMapByMapId(mapId, territoryId);
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Map flag failed");
            return false;
        }
    }

    public static string DataCenter()
    {
        try
        {
            var world = Plugin.PlayerState.CurrentWorld;
            if (!world.IsValid)
                return "";
            return world.Value.DataCenter.ValueNullable?.Name.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static float ToMap(float world, int offset, int sizeFactor)
    {
        var scaled = (world + offset) * (sizeFactor / 100f);
        return 41f * (scaled + 1024f) / 2048f + 1f;
    }

    private static string Join(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a))
            return b ?? "";
        if (string.IsNullOrWhiteSpace(b))
            return a;
        return $"{a} {b}";
    }
}
