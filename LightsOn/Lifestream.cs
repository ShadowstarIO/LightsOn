using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
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

    public static string Share(VenueLocation loc)
    {
        if (loc.IsApartment)
        {
            var sub = loc.Subdivision ? " subdivision" : "";
            return $"{loc.World}, {loc.District}, W{loc.Ward}{sub}, Apartment {loc.RoomNo}";
        }

        return $"{loc.World}, {loc.District}, W{loc.Ward}, P{loc.HousePlot}";
    }

    public static void Copy(VenueLocation loc)
    {
        try { ImGui.SetClipboardText(Share(loc)); }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Clipboard"); }
    }

    public static void Go(VenueLocation loc)
    {
        if (!Reach.CanVisitWorld(loc.World))
            return;
        var line = Share(loc);
        try
        {
            Plugin.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand").InvokeAction(line);
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Lifestream");
        }
    }
}
