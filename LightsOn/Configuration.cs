using System;
using Dalamud.Configuration;

namespace LightsOn;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool OpenUiOnLoad { get; set; }
    public bool ReportOptIn { get; set; }
    public bool HasSeenWelcome { get; set; }
    public bool ExcludeFriends { get; set; } = true;
    public bool ExcludeFreeCompany { get; set; } = true;
    public string ReporterId { get; set; } = "";
    public string OccupancyApiUrl { get; set; } = "https://lightson.wbro12-cloudflare.workers.dev";

    public void EnsureReporterId()
    {
        if (string.IsNullOrWhiteSpace(ReporterId))
            ReporterId = Guid.NewGuid().ToString("N");
    }

    public void ResetReporterId() => ReporterId = Guid.NewGuid().ToString("N");

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
