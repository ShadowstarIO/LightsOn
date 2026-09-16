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
        "Short-range street pockets. Audit and stay in the area, or wait about 10 minutes. Busier scenes note sooner.";

    public const string Welcome =
        "Tired of walking in on posted hours and an empty room? LightsOn is occupancy for listed venues: lanterns lit when there is enough company, quiet when the yard or halls look empty. Reports are optional, on the plot, and never include names or counts.";

    public static readonly string[] LogAdjectives =
    [
        "Warm", "Kind", "Gentle", "Friendly", "Cozy", "Calm", "Quiet", "Soft",
        "Bright", "Lively", "Sweet", "Lovely", "Nice", "Easy", "Smooth", "Mellow",
        "Pleasant", "Welcoming", "Relaxed", "Cheerful", "Peaceful", "Inviting", "Fine", "Great",
        "Good", "Light", "Fresh", "Happy", "Steady", "Open", "Fair", "Polite",
    ];

    public static readonly string[] LogNouns =
    [
        "host", "staff", "welcome", "music", "crowd", "room", "hall", "space",
        "vibe", "lights", "mood", "company", "energy", "scene", "bar", "floor",
        "stage", "drinks", "mix", "chat", "seats", "corner", "night", "air",
        "door", "yard", "wait", "walk", "set", "entry",
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
}
