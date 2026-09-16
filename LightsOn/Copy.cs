namespace LightsOn;

internal static class Copy
{
    public const string Happening = "Lanterns Lit";
    public const string HappeningButton = "Lanterns Lit";
    public const string Wrapped = "Quiet Halls";
    public const string WrappedButton = "Quiet Halls";
    public const string YardBusy = "Yard Busy";
    public const string YardQuiet = "Yard Quiet";
    public const string MarkedOpen = "Open Now";
    public const string NoReport = "No audit yet";
    public const string EnoughCompany = "enough company";
    public const string Quiet = "quiet";
    public const string NoPlot = "This listing has no house or apartment LightsOn can audit.";
    public const string DoorLocked = "Locked";
    public const string LooksClosed = "Looks Closed";
    public const string DirectoryUrl = "https://ffxivvenues.com/";
    public static string ListingUrl(string? venueId)
        => string.IsNullOrWhiteSpace(venueId)
            ? DirectoryUrl
            : $"https://ffxivvenues.com/venue/{venueId}";
    public const string OutdoorsHint =
        "Street pockets. Audit and stay, or wait about 10 minutes. Same area waits 5 minutes.";

    public const string Welcome =
        "Tired of walking in on posted hours and an empty room? LightsOn is occupancy for listed venues: lanterns lit when there is enough company, quiet when the yard or halls look empty. Reports are optional, on the plot, and never include names or counts.";

    public static readonly string[] LogAdjectives =
    [
        "Bright", "Calm", "Cheerful", "Cozy", "Easy", "Fair", "Fine", "Fresh",
        "Friendly", "Gentle", "Good", "Great", "Happy", "Inviting", "Kind", "Light",
        "Lively", "Lovely", "Mellow", "Nice", "Open", "Peaceful", "Pleasant", "Polite",
        "Quiet", "Relaxed", "Smooth", "Soft", "Steady", "Sweet", "Warm", "Welcoming",
    ];

    public static readonly string[] LogNouns =
    [
        "Air", "Bar", "Chat", "Company", "Corner", "Crowd", "Door", "Drinks",
        "Energy", "Entry", "Floor", "Food", "Hall", "Host", "Lights", "Mix",
        "Mood", "Music", "Night", "Room", "Scene", "Seats", "Set", "Space",
        "Staff", "Stage", "Vibe", "Wait", "Walk", "Welcome", "Yard",
    ];

    public static string LogLine(int adj, int noun)
    {
        var a = LogAdjectives[System.Math.Clamp(adj, 0, LogAdjectives.Length - 1)];
        var n = LogNouns[System.Math.Clamp(noun, 0, LogNouns.Length - 1)];
        return $"{a} {n}";
    }

    public static bool IsLogPhrase(string text)
    {
        var s = (text ?? "").Trim();
        var sp = s.IndexOf(' ');
        if (sp <= 0)
            return false;
        var a = s[..sp];
        var n = s[(sp + 1)..];
        return System.Array.IndexOf(LogAdjectives, a) >= 0 && System.Array.IndexOf(LogNouns, n) >= 0;
    }

    public static readonly string[] OutdoorScenes =
    [
        "—",
        "Camp", "Dance", "Event", "Fight", "Hunt", "Market",
        "Parade", "Party", "Performance", "RP", "Social",
    ];

    public static string OutdoorScene(int idx)
    {
        if (idx <= 0 || idx >= OutdoorScenes.Length)
            return "";
        return OutdoorScenes[idx];
    }

    public static bool IsOutdoorScene(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0)
            return true;
        return System.Array.IndexOf(OutdoorScenes, s) > 0;
    }
}
