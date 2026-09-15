using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
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
    bool Glance,
    bool Voices,
    bool Seeking,
    bool Bench,
    string Summary)
{
    public const int Threshold = Limits.Threshold;
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
            return new ScanResult(false, false, false, 0, 0, false, false, false, false, false, "Not logged in");

        var here = HousingReader.Read();
        if (!here.OnPlot)
            return new ScanResult(false, false, false, 0, 0, false, false, false, false, false, here.Summary);

        var tally = CountNearby(plugin, player, here.Inside ? 0f : Limits.YardRangeYalms);
        var met = tally.Score >= ScanResult.Threshold;
        var who = plugin.Configuration.ExcludeFriends || plugin.Configuration.ExcludeFreeCompany ? "after filters" : "nearby";
        var bits = new List<string>();
        bits.Add(met ? Copy.EnoughCompany : Copy.Quiet);
        bits.Add(who);
        if (tally.InCharacter)
            bits.Add("in character");
        if (tally.Seeking)
            bits.Add("seeking company");
        if (tally.Bench)
            bits.Add("at the bench");
        if (tally.Glance)
            bits.Add("a glance");
        if (tally.Voices)
            bits.Add("voices nearby");
        if (plugin.Session.HeardMusic)
            bits.Add("music");
        var summary = string.Join(" · ", bits) + " · " + here.Summary;
        return new ScanResult(true, here.Inside, met, tally.Score, tally.Patrons, tally.InCharacter, tally.Glance, tally.Voices, tally.Seeking, tally.Bench, summary);
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
        var tally = CountNearby(plugin, player, Limits.YardRangeYalms);
        var tier = TierName(tally.Patrons, tally.Score);
        var summary = $"{place} · {tier}";
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

    public static bool OccupancyEligible(VenueListing venue)
    {
        var loc = venue.Location;
        return loc is not null && loc.Plot is >= 1 and <= 60 && loc.Ward is >= 1 and <= 30;
    }

    private readonly record struct Crowd(
        int Visible, int Familiar, int Patrons, int Score,
        bool InCharacter, bool Seeking, bool Bench, bool Glance, bool Voices);

    private static Crowd CountNearby(Plugin plugin, IPlayerCharacter self, float maxRange)
    {
        var cfg = plugin.Configuration;
        var friends = cfg.ExcludeFriends ? FriendBook.Names() : null;
        var myTag = self.CompanyTag.TextValue.Trim();
        var visible = 0;
        var familiar = 0;
        var patrons = new List<IPlayerCharacter>();
        var inCharacter = false;
        var seeking = false;
        var bench = false;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is null || obj.ObjectKind != ObjectKind.Pc || obj is not IPlayerCharacter pc)
                continue;
            if (pc.EntityId == self.EntityId)
                continue;
            if (maxRange > 0 && Vector3.Distance(self.Position, pc.Position) > maxRange)
                continue;
            visible++;

            var name = pc.Name.TextValue;
            var tag = pc.CompanyTag.TextValue.Trim();
            var isFriend = friends is not null && friends.Contains(name);
            var isFc = cfg.ExcludeFreeCompany && myTag.Length > 0 && tag.Length > 0
                       && string.Equals(tag, myTag, StringComparison.OrdinalIgnoreCase);
            if (isFriend || isFc)
            {
                familiar++;
                continue;
            }

            patrons.Add(pc);
            if (!cfg.UseStatusSignals)
                continue;
            var status = StatusName(pc);
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

        var patronIds = new HashSet<uint>();
        foreach (var p in patrons)
            patronIds.Add(p.EntityId);

        var glance = false;
        if (cfg.UseGlanceSignals && patrons.Count > 0)
        {
            if (self.TargetObject is IPlayerCharacter look && patronIds.Contains(look.EntityId))
                glance = true;
            else
            {
                foreach (var p in patrons)
                {
                    var t = p.TargetObject;
                    if (t is null)
                        continue;
                    if (t.EntityId == self.EntityId || patronIds.Contains(t.EntityId))
                    {
                        glance = true;
                        break;
                    }
                }
            }
        }

        var voices = false;
        if (patrons.Count > 0)
        {
            var heard = plugin.Session.HeardNames;
            foreach (var p in patrons)
            {
                if (heard.Contains(NormName(p.Name.TextValue)))
                {
                    voices = true;
                    break;
                }
            }
            if (!voices && plugin.Session.SelfSpoke)
                voices = true;
        }

        var score = Math.Min(3, patrons.Count);
        if (cfg.UseStatusSignals)
        {
            if (inCharacter)
                score++;
            if (seeking)
                score++;
            if (bench)
                score++;
        }
        if (glance)
            score++;
        if (voices && (cfg.UseChatSignals || cfg.UseSaySignals))
            score++;

        return new Crowd(visible, familiar, patrons.Count, score, inCharacter, seeking, bench, glance, voices);
    }

    public static string NormName(string raw)
    {
        var s = (raw ?? "").Trim();
        var cut = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '«' or '(' or '♂' or '♀' || char.IsControl(c))
            {
                cut = i;
                break;
            }
        }
        if (cut > 0)
            s = s[..cut];
        return s.Trim().ToLowerInvariant();
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

        var here = HousingReader.Read();
        if (!here.OnPlot)
            return false;
        if (here.Ward != loc.Ward || here.Plot != loc.Plot)
            return false;
        if (here.District.Length > 0 && loc.District.Length > 0
            && HousingReader.IsKnownDistrict(here.District)
            && !string.Equals(here.District, loc.District, StringComparison.OrdinalIgnoreCase))
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
        var here = HousingReader.Read();
        if (!here.OnPlot)
            return "";
        return $"{CurrentWorldName()}|{here.Ward}|{here.Plot}";
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
