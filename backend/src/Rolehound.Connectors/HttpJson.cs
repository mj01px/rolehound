using System.Net;
using System.Text.Json;
using Rolehound.Scoring;

namespace Rolehound.Connectors;

/// <summary>GET with a few retries on transient failures (429 and 5xx), then a clear error.</summary>
internal static class HttpJson
{
    private const int Attempts = 3;

    public static async Task<JsonDocument> GetAsync(
        HttpClient http, Uri url, string subject, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        var body = await GetTextAsync(http, url, subject, ct, headers);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("source_invalid", $"{subject}: resposta fora do formato esperado.", ex);
        }
    }

    public static async Task<string> GetTextAsync(
        HttpClient http, Uri url, string subject, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt < Attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                    continue;
                }

                throw new ConnectorException("source_unreachable", $"{subject}: sem resposta da fonte.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ConnectorException("target_not_found", $"{subject}: não encontrado na fonte.");
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (transient && attempt < Attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ConnectorException("source_error", $"{subject}: a fonte respondeu {(int)response.StatusCode}.");
                }

                return await response.Content.ReadAsStringAsync(ct);
            }
        }
    }

    public static string? String(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static WorkMode? WorkModeOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        "on-site" or "onsite" or "presential" => WorkMode.Onsite,
        _ => null,
    };
}
