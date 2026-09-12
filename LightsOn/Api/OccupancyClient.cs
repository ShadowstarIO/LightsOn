using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LightsOn.Api;

internal sealed class OccupancyClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient http;

    public OccupancyClient(HttpClient http) => this.http = http;

    public static bool IsUsable(string? baseUrl) =>
        Uri.TryCreate((baseUrl ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.Host.IndexOf('.') >= 0;

    public async Task<Dictionary<string, OccupancySnapshot>> GetOccupancy(string baseUrl, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/occupancy";
        var list = await http.GetFromJsonAsync<List<OccupancySnapshot>>(url, Json, token).ConfigureAwait(false);
        var map = new Dictionary<string, OccupancySnapshot>(StringComparer.Ordinal);
        if (list is null)
            return map;
        foreach (var row in list)
        {
            if (!string.IsNullOrWhiteSpace(row.VenueId))
                map[row.VenueId] = row;
        }
        return map;
    }

    public async Task<List<OccupancyEvent>> GetReportLog(string baseUrl, string venueId, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/reports?venueId=" + Uri.EscapeDataString(venueId);
        return await http.GetFromJsonAsync<List<OccupancyEvent>>(url, Json, token).ConfigureAwait(false) ?? [];
    }

    public async Task PostReport(string baseUrl, OccupancyReport report, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/reports";
        using var res = await http.PostAsJsonAsync(url, report, Json, token).ConfigureAwait(false);
        await EnsureOk(res).ConfigureAwait(false);
    }

    public async Task<List<GuestNote>> GetNotes(string baseUrl, string venueId, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/notes?venueId=" + Uri.EscapeDataString(venueId);
        return await http.GetFromJsonAsync<List<GuestNote>>(url, Json, token).ConfigureAwait(false) ?? [];
    }

    public async Task PostNote(string baseUrl, NotePost note, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/notes";
        using var res = await http.PostAsJsonAsync(url, note, Json, token).ConfigureAwait(false);
        await EnsureOk(res).ConfigureAwait(false);
    }

    public async Task<List<OutdoorSnapshot>> GetOutdoors(string baseUrl, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/outdoors";
        return await http.GetFromJsonAsync<List<OutdoorSnapshot>>(url, Json, token).ConfigureAwait(false) ?? [];
    }

    public async Task PostOutdoor(string baseUrl, OutdoorReport report, CancellationToken token)
    {
        var url = baseUrl.Trim().TrimEnd('/') + "/v1/outdoors";
        using var res = await http.PostAsJsonAsync(url, report, Json, token).ConfigureAwait(false);
        await EnsureOk(res).ConfigureAwait(false);
    }

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode)
            return;
        var raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        var msg = "server error";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
                msg = err.GetString() ?? msg;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(raw) && raw.Length < 200)
                msg = raw.Trim();
        }
        throw new HttpRequestException(msg, null, res.StatusCode);
    }
}
