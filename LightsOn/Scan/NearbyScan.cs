using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using LightsOn.Api;
using Lumina.Excel.Sheets;

namespace LightsOn.Scan;

public readonly record struct ScanResult(
    bool OnPlot,
    bool Inside,
    bool ThresholdMet,
    int Score,
    int Patrons,
    bool InCharacter,
    string Summary)
{
    public const int Threshold = 3;
}

public readonly record struct OutdoorScan(
    string Pocket,
    string World,
    string Place,
    int Patrons,
    int Score,
    bool InCharacter,
    int Visible,
    int Familiar,
    string Tier,
    string Summary)
{
    public bool AskPrivate => Visible >= 3 && Familiar * 2 >= Visible;
}

internal static class NearbyScan
{
    public static ScanResult Run(Plugin plugin)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return new ScanResult(false, false, false, 0, 0, false, "Not logged in");

        var here = HousingReader.Read(CurrentZoneName());
        if (!here.OnPlot)
            return new ScanResult(false, false, false, 0, 0, false, here.Summary);

        var tally = CountNearby(plugin, player.EntityId, player.CompanyTag.TextValue.Trim());
        var met = tally.Score >= ScanResult.Threshold;
        var who = plugin.Configuration.ExcludeFriends || plugin.Configuration.ExcludeFreeCompany ? "after filters" : "nearby";
        var bits = new List<string>();
        if (met)
            bits.Add(Copy.EnoughCompany);
        else
            bits.Add(Copy.Quiet);
        bits.Add(who);
        if (tally.InCharacter)
            bits.Add("in character");
        if (tally.Seeking)
            bits.Add("seeking company");
        if (tally.Bench)
            bits.Add("at the bench");
        var summary = string.Join(" · ", bits) + " · " + here.Summary;
        return new ScanResult(true, here.Inside, met, tally.Score, tally.Patrons, tally.InCharacter, summary);
    }

    public static OutdoorScan RunOutdoor(Plugin plugin)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return default;

        var world = CurrentWorldName();
        var place = CurrentZoneName();
        var pos = player.Position;
        var gx = (int)MathF.Floor(pos.X / 20f);
        var gz = (int)MathF.Floor(pos.Z / 20f);
        var pocket = $"{world}|{Plugin.ClientState.TerritoryType}|{gx}|{gz}";
        var tally = CountNearby(plugin, player.EntityId, player.CompanyTag.TextValue.Trim());
        var tier = TierName(tally.Patrons, tally.Score);
        var summary = $"{place} · {tier} · {tally.Patrons} patrons scored";
        return new OutdoorScan(pocket, world, place, tally.Patrons, tally.Score, tally.InCharacter, tally.Visible, tally.Familiar, tier, summary);
    }

    public static string TierName(int patrons, int score)
    {
        if (patrons >= 8 || score >= 6)
            return "extremely_busy";
        if (patrons >= 4 || score >= 4)
            return "some_activity";
        if (patrons >= 1 || score >= 1)
            return "some_wandering";
        return "";
    }

    public static string TierLabel(string tier) => tier switch
    {
        "extremely_busy" => "Extremely busy",
        "some_activity" => "Some activity",
        "some_wandering" => "Some wandering",
        _ => "Quiet",
    };

    private readonly record struct Tally(int Visible, int Familiar, int Patrons, int Score, bool InCharacter, bool Seeking, bool Bench);

    private static Tally CountNearby(Plugin plugin, uint selfId, string myTag)
    {
        var cfg = plugin.Configuration;
        var friends = cfg.ExcludeFriends ? FriendBook.Names() : null;
        var visible = 0;
        var familiar = 0;
        var patrons = 0;
        var inCharacter = false;
        var seeking = false;
        var bench = false;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is null || obj.ObjectKind != ObjectKind.Pc)
                continue;
            if (obj.EntityId == selfId)
                continue;
            visible++;

            var name = obj.Name.TextValue;
            var tag = obj is IPlayerCharacter pc ? pc.CompanyTag.TextValue.Trim() : "";
            var isFriend = friends is not null && friends.Contains(name);
            var isFc = cfg.ExcludeFreeCompany && myTag.Length > 0 && tag.Length > 0
                       && string.Equals(tag, myTag, StringComparison.OrdinalIgnoreCase);
            if (isFriend || isFc)
            {
                familiar++;
                continue;
            }

            patrons++;
            if (!cfg.UseStatusSignals || obj is not IPlayerCharacter player)
                continue;
            var status = StatusName(player);
            if (status.Contains("role-playing", StringComparison.OrdinalIgnoreCase)
                || status.Contains("roleplaying", StringComparison.OrdinalIgnoreCase))
                inCharacter = true;
            else if (status.Contains("looking for party", StringComparison.OrdinalIgnoreCase)
                     || status.Contains("party finder", StringComparison.OrdinalIgnoreCase)
                     || status.Contains("recruiting", StringComparison.OrdinalIgnoreCase))
                seeking = true;
            else if (status.Contains("meld", StringComparison.OrdinalIgnoreCase))
                bench = true;
        }

        var score = Math.Min(3, patrons);
        if (cfg.UseStatusSignals)
        {
            if (inCharacter)
                score++;
            if (seeking)
                score++;
            if (bench)
                score++;
        }

        return new Tally(visible, familiar, patrons, score, inCharacter, seeking, bench);
    }

    private static string StatusName(IPlayerCharacter player)
    {
        try
        {
            return player.OnlineStatus.ValueNullable?.Name.ToString() ?? "";
        }
        catch
        {
            return "";
        }
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

    public static VenueListing? ListedHere(IEnumerable<VenueListing> venues)
    {
        foreach (var venue in venues)
        {
            if (MatchesVenue(venue))
                return venue;
        }
        return null;
    }

    public static string PlotKey()
    {
        var here = HousingReader.Read(CurrentZoneName());
        if (!here.OnPlot)
            return "";
        return $"{CurrentWorldName()}|{here.District}|{here.Ward}|{here.Plot}|{(here.Subdivision ? 1 : 0)}";
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
