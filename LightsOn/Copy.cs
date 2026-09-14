namespace LightsOn;

internal static class Copy
{
    public const string Happening = "Lanterns lit";
    public const string HappeningButton = "Lanterns are lit";
    public const string Wrapped = "Wrapped up";
    public const string WrappedButton = "Wrapped up";
    public const string MarkedOpen = "Open now";
    public const string NoReport = "No occupancy yet";
    public const string EnoughCompany = "enough company";
    public const string Quiet = "quiet";
    public const string NoPlot = "No plot on this listing — occupancy is for Ward + Plot houses.";
    public const string DoorLocked = "Door is locked";
    public const string MixedReports =
        "People inside, and a quiet yard, in the same window. The door may be locked to the street — judge for yourself.";

    public const string Welcome =
        "Tired of walking in on posted hours and an empty room? LightsOn is occupancy for listed venues: lanterns lit when there is enough company, wrapped up when it looks quiet. Reports are optional, on the plot, and never include names or counts.";

    public const string ReportsBlurb =
        "Off until you opt in. LightsOn never sends names, IDs, or how many patrons it saw. You can report the yard and the room separately — the list shows both.";

    public const string LogBookHint =
        "Pick a short line from the list. Lanterns must be lit, and you need a little time on the plot. Not a free-form review.";

    public const string OutdoorsHint =
        "Outdoor scenes in a short pocket around you — not a whole city. After about 10 minutes in the same pocket, LightsOn can note how lively it feels. Friends and Free Company can be left out. Private gatherings can be hidden.";

    public static readonly string[] LogPhrases =
    [
        "Kind host",
        "Great music",
        "Warm crowd",
        "Quiet corner",
        "Come again",
        "Short wait",
        "Fine drinks",
        "Good floor",
        "Friendly door",
        "Worth the walk",
    ];
}
