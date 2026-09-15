namespace LightsOn;

internal static class Copy
{
    public const string Happening = "Lanterns lit";
    public const string HappeningButton = "Lanterns lit";
    public const string Wrapped = "Halls are quiet";
    public const string WrappedButton = "Quiet halls";
    public const string YardBusy = "Yard busy";
    public const string YardQuiet = "Yard quiet";
    public const string MarkedOpen = "Open now";
    public const string NoReport = "No occupancy yet";
    public const string EnoughCompany = "enough company";
    public const string Quiet = "quiet";
    public const string NoPlot = "No plot on this listing — occupancy is for Ward + Plot houses.";
    public const string DoorLocked = "locked";
    public const string MixedReports =
        "Yard and room don't agree. LightsOn leans toward whichever side has more weight — the room counts extra.";

    public const string Welcome =
        "Tired of walking in on posted hours and an empty room? LightsOn is occupancy for listed venues: lanterns lit when there is enough company, quiet when the yard or halls look empty. Reports are optional, on the plot, and never include names or counts.";

    public const string ReportsBlurb =
        "Off until you opt in. LightsOn never sends names, IDs, or how many patrons it saw. Quiet is always manual. Lanterns try to send themselves when the scan is enough.";

    public const string LogBookHint =
        "Pick an adjective and a noun. Lanterns must be lit, posted hours, and a short wait on the property. Not a free-form review.";

    public const string OutdoorsHint =
        "Outdoor scenes in a short pocket around you — not a whole city. After about 10 minutes in the same pocket, LightsOn can note how lively it feels. Friends and Free Company can be left out. Private gatherings can be hidden.";

    public static readonly string[] LogAdjectives =
    [
        "Kind", "Warm", "Quiet", "Lively", "Great", "Fine", "Friendly", "Soft", "Bright", "Worth",
    ];

    public static readonly string[] LogNouns =
    [
        "host", "music", "crowd", "corner", "wait", "drinks", "floor", "door", "walk", "hall",
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
