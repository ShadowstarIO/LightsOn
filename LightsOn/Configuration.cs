using System;
using Dalamud.Configuration;

namespace LightsOn;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool OpenUiOnLoad { get; set; }
    public bool ReportOptIn { get; set; }
    public bool HasSeenWelcome { get; set; }
    public bool ExcludeFriends { get; set; } = true;
    public bool ExcludeFreeCompany { get; set; } = true;
    public bool AutoHappening { get; set; } = true;
    public bool PromptOnEnter { get; set; } = true;
    public bool AllowLogBook { get; set; } = true;
    public bool UseStatusSignals { get; set; } = true;
    public bool NoteOutdoorScenes { get; set; }
    public string ReporterId { get; set; } = "";
    public string OccupancyApiUrl { get; set; } = "https://lightson.wbro12-cloudflare.workers.dev";
    public long ReportEnabledAtUnix { get; set; }

    public DateTimeOffset ReportEnabledAt =>
        ReportEnabledAtUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ReportEnabledAtUnix)
            : DateTimeOffset.MinValue;

    public void SetReportOptIn(bool on)
    {
        if (on && !ReportOptIn)
            ReportEnabledAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ReportOptIn = on;
    }

    public void EnsureReporterId()
    {
        if (string.IsNullOrWhiteSpace(ReporterId))
            ReporterId = Guid.NewGuid().ToString("N");
    }

    public void ResetReporterId() => ReporterId = Guid.NewGuid().ToString("N");

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
