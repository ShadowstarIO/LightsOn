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
        cache = list ?? [];
        fetchedAt = DateTimeOffset.UtcNow;
        return cache;
    }
}
