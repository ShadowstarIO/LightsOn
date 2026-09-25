using System.Text.RegularExpressions;
using LightsOn.Api;

namespace LightsOn;

internal static class PlaceId
{
    private static readonly Regex IdPattern = new("^[A-Z]{4}-[A-Z]{4}-W\\d{2}(?:-S)?-[AP]\\d{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex Letters = new("[^A-Za-z]", RegexOptions.CultureInvariant);
    private static readonly Regex Lb = new(@"(^|[^a-z])lb([^a-z]|$)", RegexOptions.CultureInvariant);
    private static readonly Regex Emp = new(@"(^|[^a-z])emp([^a-z]|$)", RegexOptions.CultureInvariant);

    public static bool IsPlace(string? id) =>
        !string.IsNullOrWhiteSpace(id) && IdPattern.IsMatch(id);

    public static string From(VenueLocation? loc)
    {
        if (loc is null)
            return "";
        return From(loc.World, loc.District, loc.Ward, loc.Plot, loc.RoomNo, loc.Subdivision);
    }

    public static string From(string? world, string? district, int ward, int plot, int apartment, bool subdivision)
    {
        var w = WorldCode(world);
        var d = DistrictCode(district);
        var plotN = plot;
        var sub = subdivision;
        if (plotN is >= 31 and <= 60)
        {
            sub = true;
            plotN -= 30;
        }
        if (w.Length < 4 || d.Length == 0 || ward is < 1 or > 30)
            return "";
        var subSlot = sub ? "-S" : "";
        var wardSlot = ward.ToString("00");
        if (apartment is >= 1 and <= 99)
            return $"{w}-{d}-W{wardSlot}{subSlot}-A{apartment:00}";
        if (plotN is < 1 or > 30)
            return "";
        return $"{w}-{d}-W{wardSlot}{subSlot}-P{plotN:00}";
    }

    private static string WorldCode(string? world)
    {
        if (string.IsNullOrWhiteSpace(world))
            return "";
        var letters = Letters.Replace(world, "").ToUpperInvariant();
        return letters.Length <= 4 ? letters : letters[..4];
    }

    private static string DistrictCode(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (t.Contains("lavender") || Lb.IsMatch(t))
            return "LAVB";
        if (t.Contains("goblet"))
            return "GOBL";
        if (t.Contains("shirogane") || t.Contains("kobai"))
            return "SHIR";
        if (t.Contains("empyreum") || t.Contains("empyrium") || Emp.IsMatch(t))
            return "EMPY";
        if (t.Contains("mist") || t.Contains("topmast"))
            return "MIST";
        return "";
    }
}
