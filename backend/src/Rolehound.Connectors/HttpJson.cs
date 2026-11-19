using System.Net;
using System.Text.Json;

namespace Rolehound.Connectors;

/// <summary>GET with a few retries on transient failures (429 and 5xx), then a clear error.</summary>
internal static class HttpJson
{
    private const int Attempts = 3;

    public static async Task<JsonDocument> GetAsync(HttpClient http, Uri url, string subject, CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        wait ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt < Attempts)
                {
                    await wait(TimeSpan.FromSeconds(attempt), ct);
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
                    await wait(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ConnectorException("source_error", $"{subject}: a fonte respondeu {(int)response.StatusCode}.");
                }

                try
                {
                    return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                }
                catch (JsonException ex)
                {
                    throw new ConnectorException("source_invalid", $"{subject}: resposta fora do formato esperado.", ex);
                }
            }
        }
    }

    public static string? String(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
