using System;
using System.Linq;
using LightsOn.Api;

namespace LightsOn;

internal static class Lifestream
{
    public static bool Installed()
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins.Any(p =>
                p.IsLoaded && string.Equals(p.InternalName, "Lifestream", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static void Go(VenueLocation loc)
    {
        var place = loc.Apartment > 0 || loc.Room > 0
            ? $"w{loc.Ward} {(loc.Apartment > 0 ? loc.Apartment : loc.Room)}"
            : $"w{loc.Ward} p{loc.Plot}";
        var args = $"{loc.World}, {loc.District}, {place}";
        Plugin.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand").InvokeAction(args);
    }
}
