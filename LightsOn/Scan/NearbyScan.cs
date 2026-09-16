using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
    bool Emotes,
    string Summary)
{
    public const int Threshold = Limits.Threshold;
}

public readonly record struct OutdoorScan(
    string Pocket,
    string World,
    string Place,
    string Zone,
    string Kind,
    int Patrons,
    int ZoneCount,
    int Score,
    bool InCharacter,
    bool Glance,
    bool Voices,
    bool Emotes,
    int Visible,
    int Familiar,
    string Tier,
    string Summary,
    Vector3 Origin,
    float MapX,
    float MapY)
{
    public bool AskPrivate => Visible >= 3 && Familiar * 2 >= Visible;
}

internal static class NearbyScan
{
    public static ScanResult Run(Plugin plugin)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return new ScanResult(false, false, false, 0, 0, false, false, false, false, false, false, "Not logged in");

        var here = HousingReader.Read();
        if (!here.OnProperty)
            return new ScanResult(false, false, false, 0, 0, false, false, false, false, false, false, here.Summary);

        var tally = CountNearby(plugin, player, here.Inside || here.OnApartment ? 0f : Limits.YardRangeYalms);
        var met = tally.Score >= ScanResult.Threshold;
        var who = plugin.Configuration.ExcludeFriends || plugin.Configuration.ExcludeFreeCompany ? "after filters" : "nearby";
        var bits = new List<string>();
        bits.Add(met ? Copy.EnoughCompany : Copy.Quiet);
        bits.Add(who);
        if (tally.InCharacter)
            bits.Add("in character");
        if (tally.Seeking)
            bits.Add("Party Finder");
        if (tally.Bench)
            bits.Add("melding");
        if (tally.Glance)
            bits.Add("a glance");
        if (tally.Voices)
            bits.Add("voices nearby");
        if (tally.Emotes)
            bits.Add("emotes");
        if (plugin.Session.HeardMusic)
            bits.Add("music");
        var summary = string.Join(" · ", bits) + " · " + here.Summary;
        return new ScanResult(true, here.Inside || here.OnApartment, met, tally.Score, tally.Patrons, tally.InCharacter,
            tally.Glance, tally.Voices, tally.Seeking, tally.Bench, tally.Emotes, summary);
    }

    public static OutdoorScan RunOutdoor(Plugin plugin)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return new OutdoorScan("", "", "", "", "", 0, 0, 0, false, false, false, false, 0, 0, "", "", default, 0, 0);

        var world = CurrentWorldName();
        var (region, place, kind) = Zone.Describe(Plugin.ClientState.TerritoryType);
        if (place.Length == 0)
            place = CurrentZoneName();
        var pos = player.Position;
        var cell = Limits.OutdoorCellYalms;
        var gx = (int)MathF.Floor(pos.X / cell);
        var gz = (int)MathF.Floor(pos.Z / cell);
        var pocket = $"{world}|{Plugin.ClientState.TerritoryType}|{gx}|{gz}";
        var (tally, zoneCount) = CountOutdoor(plugin, player);
        var tier = OutdoorTier(tally.Patrons, zoneCount);
        var (mx, my) = Here.WorldToMap(Plugin.ClientState.TerritoryType, pos.X, pos.Z);
        var coords = mx > 0 && my > 0 ? $" ({mx:0.0}, {my:0.0})" : "";
        var summary = $"{place}{coords} · {TierLabel(tier)} · {Zone.CountLabel(tally.Patrons)} nearby";
        return new OutdoorScan(pocket, world, place, region, kind, tally.Patrons, zoneCount, tally.Score,
            tally.InCharacter, tally.Glance, tally.Voices, tally.Emotes, tally.Visible, tally.Familiar,
            tier, summary, pos, mx, my);
    }

    public static bool TryParsePocket(string pocket, out string world, out uint territory, out int gx, out int gz)
    {
        world = "";
        territory = 0;
        gx = 0;
        gz = 0;
        var parts = (pocket ?? "").Split('|');
        if (parts.Length != 4)
            return false;
        world = parts[0];
        if (!uint.TryParse(parts[1], out territory))
            return false;
        if (!int.TryParse(parts[2], out gx) || !int.TryParse(parts[3], out gz))
            return false;
        return world.Length > 0 && territory > 0;
    }

    public static string PocketCoords(string pocket)
    {
        if (!TryParsePocket(pocket, out _, out var territory, out var gx, out var gz))
            return "";
        var (mx, my) = Here.WorldToMap(territory, (gx + 0.5f) * Limits.OutdoorCellYalms, (gz + 0.5f) * Limits.OutdoorCellYalms);
        return mx > 0 && my > 0 ? $"({mx:0.0}, {my:0.0})" : "";
    }

    public static int TierRank(string? tier) => tier switch
    {
        "extremely_busy" => 5,
        "busy" => 4,
        "some_activity" => 3,
        "light_activity" => 2,
        "some_wandering" => 1,
        _ => 0,
    };

    public static int LockMinutes(string? tier) => tier switch
    {
        "extremely_busy" => Limits.OutdoorLockBusyMinutes,
        "busy" => 6,
        "some_activity" => Limits.OutdoorLockActivityMinutes,
        "light_activity" => 12,
        _ => Limits.OutdoorLockWanderingMinutes,
    };

    public static int WatchSeconds(string? tier) => TierRank(tier) switch
    {
        5 => Limits.OutdoorWatchBusySeconds,
        4 => 25,
        3 => Limits.OutdoorWatchSomeSeconds,
        2 => 45,
        _ => Limits.OutdoorWatchSeconds,
    };

    public static bool NearbyPockets(string? a, string? b)
    {
        if (!TryParsePocket(a ?? "", out var wa, out var ta, out var gxa, out var gza))
            return false;
        if (!TryParsePocket(b ?? "", out var wb, out var tb, out var gxb, out var gzb))
            return false;
        if (!string.Equals(wa, wb, StringComparison.OrdinalIgnoreCase) || ta != tb)
            return false;
        return Math.Max(Math.Abs(gxa - gxb), Math.Abs(gza - gzb)) <= 2;
    }

    public static string OutdoorTier(int nearby, int zone)
    {
        nearby = Math.Min(99, nearby);
        zone = Math.Min(99, zone);
        if (nearby >= 50 || zone >= 85)
            return "extremely_busy";
        if (nearby >= 28 || zone >= 55)
            return "busy";
        if (nearby >= 16 || zone >= 35)
            return "some_activity";
        if (nearby >= 8 || zone >= 18)
            return "light_activity";
        if (nearby >= 4)
            return "some_wandering";
        return "";
    }

    public static string TierLabel(string? tier) => tier switch
    {
        "extremely_busy" => "Extremely Busy",
        "busy" => "Busy",
        "some_activity" => "Some Activity",
        "light_activity" => "Light Activity",
        "some_wandering" => "Some Wandering",
        _ => "Quiet",
    };

    public static bool OccupancyEligible(VenueListing venue)
    {
        var loc = venue.Location;
        if (loc is null || loc.Ward is < 1 or > 30)
            return false;
        if (loc.IsApartment)
            return loc.RoomNo is >= 1 and <= 99;
        return loc.HousePlot is >= 1 and <= 60;
    }

    private readonly record struct Crowd(
        int Visible, int Familiar, int Patrons, int Score,
        bool InCharacter, bool Seeking, bool Bench, bool Glance, bool Voices, bool Emotes);

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
        var liveEmote = false;

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
            if (cfg.UseEmoteSignals && IsEmoting(pc))
                liveEmote = true;
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

        var emotes = liveEmote || (cfg.UseEmoteSignals && plugin.Session.HeardEmote && patrons.Count > 0);

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
        if (emotes)
            score++;

        return new Crowd(visible, familiar, patrons.Count, score, inCharacter, seeking, bench, glance, voices, emotes);
    }

    private static (Crowd Nearby, int Zone) CountOutdoor(Plugin plugin, IPlayerCharacter self)
    {
        var nearby = CountNearby(plugin, self, Limits.OutdoorRangeYalms);
        var zone = 0;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is null || obj.ObjectKind != ObjectKind.Pc || obj is not IPlayerCharacter pc)
                continue;
            if (pc.EntityId == self.EntityId)
                continue;
            zone++;
            if (zone >= 99)
                break;
        }
        return (nearby, zone);
    }

    private static bool IsEmoting(IPlayerCharacter pc)
    {
        try
        {
            unsafe
            {
                var ch = (Character*)pc.Address;
                return ch->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;
            }
        }
        catch
        {
            return false;
        }
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

    public static bool MatchesVenue(VenueListing? venue)
    {
        var loc = venue?.Location;
        if (loc is null)
            return false;
        var world = loc.World ?? "";
        var district = loc.District ?? "";
        if (!string.Equals(CurrentWorldName(), world.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var here = HousingReader.Read();
        var hereDistrict = here.District ?? "";
        if (district.Length > 0 && hereDistrict.Length > 0
            && !HousingReader.SameDistrict(hereDistrict, district))
            return false;
        if (here.Ward != loc.Ward)
            return false;

        if (loc.IsApartment)
        {
            if (!here.OnApartment)
                return false;
            if (here.Apartment != loc.RoomNo)
                return false;
            return here.Subdivision == loc.Subdivision;
        }

        if (!here.OnHouse)
            return false;
        return here.Plot == loc.HousePlot;
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
        if (here.OnApartment)
            return $"{CurrentWorldName()}|{here.Ward}|A{here.Apartment}|{(here.Subdivision ? 1 : 0)}";
        if (here.OnHouse)
            return $"{CurrentWorldName()}|{here.Ward}|{here.Plot}";
        return "";
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
