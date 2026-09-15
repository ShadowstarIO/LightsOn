using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LightsOn.Api;

internal sealed class DirectoryClient
{
    public const string VenuesUrl = "https://api.ffxivvenues.com/venue";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient http;
    private DateTimeOffset fetchedAt = DateTimeOffset.MinValue;
    private List<VenueListing> cache = [];

    public DirectoryClient(HttpClient http) => this.http = http;

    public IReadOnlyList<VenueListing> Cached => cache;

    public async Task<IReadOnlyList<VenueListing>> GetVenues(bool force, CancellationToken token)
    {
        if (!force && cache.Count > 0 && DateTimeOffset.UtcNow - fetchedAt < TimeSpan.FromMinutes(10))
            return cache;

        var list = await http.GetFromJsonAsync<List<VenueListing>>(VenuesUrl, Json, token).ConfigureAwait(false);
        cache = [];
        foreach (var venue in list ?? [])
        {
            if (venue is null)
                continue;
            venue.Id ??= "";
            venue.Name ??= "";
            if (venue.Location is not null)
            {
                venue.Location.DataCenter ??= "";
                venue.Location.World ??= "";
                venue.Location.District ??= "";
                var zone = HousingReader.ResolveDistrict(venue.Location.District);
                if (HousingReader.IsKnownDistrict(zone))
                    venue.Location.District = zone;
                if (venue.Location.RoomNo == 0)
                    venue.Location.Plot = HousingReader.CanonicalPlot(venue.Location.Plot, venue.Location.Subdivision);
            }
            venue.Description ??= [];
            venue.Tags ??= [];
            venue.Occupancy ??= OccupancySnapshot.Unknown;
            venue.BindHours();
            cache.Add(venue);
        }
        fetchedAt = DateTimeOffset.UtcNow;
        return cache;
    }
}
