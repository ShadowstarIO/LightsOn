using System;
using Dalamud.Configuration;

namespace LightsOn;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    public bool OpenUiOnLoad { get; set; }
    public bool ReportOptIn { get; set; }
    public bool HasSeenWelcome { get; set; }
    public bool ExcludeFriends { get; set; } = true;
    public bool ExcludeFreeCompany { get; set; } = true;
    public bool AutoHappening { get; set; } = true;
    public bool PromptOnEnter { get; set; } = true;
    public bool ClosePlotOnLeave { get; set; } = true;
    public bool AllowLogBook { get; set; } = true;
    public bool UseStatusSignals { get; set; } = true;
    public bool UseGlanceSignals { get; set; } = true;
    public bool UseChatSignals { get; set; } = true;
    public bool UseSaySignals { get; set; }
    public bool NoteOutdoorScenes { get; set; }
    public bool ListingsOnly { get; set; }
    public bool ShowOtherRegions { get; set; }
    public string ReporterId { get; set; } = "";
    public string OccupancyApiUrl { get; set; } = "https://REDACTED";
    public long ReportEnabledAtUnix { get; set; }
    public long ReporterResetAtUnix { get; set; }

    public DateTimeOffset ReportEnabledAt =>
        ReportEnabledAtUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ReportEnabledAtUnix)
            : DateTimeOffset.MinValue;

    public DateTimeOffset ReporterResetAt =>
        ReporterResetAtUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ReporterResetAtUnix)
            : DateTimeOffset.MinValue;

    public bool OccupancyEnabled => !ListingsOnly;

    public TimeSpan ReporterResetLockRemaining
    {
        get
        {
            if (ReporterResetAtUnix <= 0)
                return TimeSpan.Zero;
            var left = TimeSpan.FromMinutes(Limits.ResetLockMinutes) - (DateTimeOffset.UtcNow - ReporterResetAt);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

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

    public void ResetReporterId()
    {
        ReporterId = Guid.NewGuid().ToString("N");
        ReporterResetAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
