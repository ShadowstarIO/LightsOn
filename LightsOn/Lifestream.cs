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

        var plot = loc.HousePlot;
        return $"{loc.World}, {loc.District}, W{loc.Ward}, P{plot}";
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
        if (TryGoIpc(loc))
            return;
        var place = loc.IsApartment
            ? $"w{loc.Ward} {loc.RoomNo}"
            : $"w{loc.Ward} p{loc.HousePlot}";
        var args = $"{loc.World}, {loc.District}, {place}";
        Plugin.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand").InvokeAction(args);
    }

    private static bool TryGoIpc(VenueLocation loc)
    {
        try
        {
            var build = Plugin.PluginInterface.GetIpcSubscriber<string, string, string, string, bool, bool, object>(
                "Lifestream.BuildAddressBookEntry");
            var go = Plugin.PluginInterface.GetIpcSubscriber<object, object>("Lifestream.GoToHousingAddress");
            var num = loc.IsApartment ? loc.RoomNo.ToString() : loc.HousePlot.ToString();
            var entry = build.InvokeFunc(
                loc.World, loc.District, loc.Ward.ToString(), num, loc.IsApartment, loc.Subdivision);
            go.InvokeAction(entry);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Lifestream housing IPC");
            return false;
        }
    }
}
