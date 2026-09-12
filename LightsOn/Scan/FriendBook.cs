using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace LightsOn.Scan;

internal static class FriendBook
{
    public static HashSet<string> Names()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            unsafe
            {
                var proxy = InfoProxyFriendList.Instance();
                if (proxy == null)
                    return names;
                foreach (ref var entry in proxy->CharDataSpan)
                {
                    if (entry.ContentId == 0)
                        continue;
                    var name = entry.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Friend list read failed");
        }

        return names;
    }
}
