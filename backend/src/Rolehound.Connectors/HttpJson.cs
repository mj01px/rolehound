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

    public static Task<string> GetTextAsync(
        HttpClient http, Uri url, string subject, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(http, HttpMethod.Get, url, null, subject, headers, ct);

    /// <summary>POST a JSON body and read JSON back, with the same retries and errors as a GET.</summary>
    public static async Task<JsonDocument> PostAsync(
        HttpClient http, Uri url, object body, string subject, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        var text = await SendAsync(http, HttpMethod.Post, url, JsonSerializer.Serialize(body), subject, headers, ct);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("source_invalid", $"{subject}: resposta fora do formato esperado.", ex);
        }
    }

    private static async Task<string> SendAsync(
        HttpClient http, HttpMethod method, Uri url, string? json, string subject, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (json is not null)
            {
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

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

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new ConnectorException("target_closed", $"{subject}: a fonte pede login para mostrar as vagas.");
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
