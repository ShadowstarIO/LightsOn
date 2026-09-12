using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using LightsOn.Api;
using Lumina.Excel.Sheets;

namespace LightsOn.Scan;

public readonly record struct ScanResult(
    bool OnPlot,
    bool Inside,
    bool ThresholdMet,
    string Summary)
{
    public const int Threshold = 3;
}

internal static class NearbyScan
{
    public static ScanResult Run(Plugin plugin)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return new ScanResult(false, false, false, "Not logged in");

        var here = HousingReader.Read(CurrentZoneName());
        if (!here.OnPlot)
            return new ScanResult(false, false, false, here.Summary);

        var excludeFriends = plugin.Configuration.ExcludeFriends;
        var excludeFc = plugin.Configuration.ExcludeFreeCompany;
        var friends = excludeFriends ? FriendBook.Names() : null;
        var myTag = excludeFc ? player.CompanyTag.TextValue.Trim() : "";

        var counted = 0;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is null || obj.ObjectKind != ObjectKind.Pc)
                continue;
            if (obj.EntityId == player.EntityId)
                continue;
            if (friends is not null && friends.Contains(obj.Name.TextValue))
                continue;
            if (excludeFc && myTag.Length > 0)
            {
                var tag = obj is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc
                    ? pc.CompanyTag.TextValue.Trim()
                    : "";
                if (tag.Length > 0 && string.Equals(tag, myTag, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            counted++;
        }

        var who = excludeFriends || excludeFc ? "after filters" : "nearby";
        var met = counted >= ScanResult.Threshold;
        var summary = met
            ? $"{Copy.EnoughCompany} ({who}) · {here.Summary}"
            : $"{Copy.Quiet} ({who}) · {here.Summary}";
        return new ScanResult(true, here.Inside, met, summary);
    }

    public static bool MatchesVenue(VenueListing venue)
    {
        var loc = venue.Location;
        if (loc is null)
            return false;
        if (!string.Equals(CurrentWorldName(), loc.World, StringComparison.OrdinalIgnoreCase))
            return false;

        var here = HousingReader.Read(CurrentZoneName());
        if (!here.OnPlot)
            return false;
        if (!string.Equals(here.District, loc.District, StringComparison.OrdinalIgnoreCase))
            return false;
        if (here.Ward != loc.Ward || here.Plot != loc.Plot)
            return false;
        if (loc.Subdivision && !here.Subdivision)
            return false;
        return true;
    }

    public static string CurrentWorldName()
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

    public static string CurrentZoneName()
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
            var row = sheet.GetRowOrDefault(Plugin.ClientState.TerritoryType);
            return row?.PlaceName.ValueNullable?.Name.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
