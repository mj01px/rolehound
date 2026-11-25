using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for https://openrouter.ai/api/v1: one valid key, one model that
/// does not exist, and canned answers per structured output name.
/// </summary>
public sealed class FakeOpenRouter : HttpMessageHandler
{
    public const string ValidKey = "sk-or-v1-test-key-0000000000000001";
    public const string BrokenModel = "nobody/missing-model";

    /// <summary>A management key: reads /credits, cannot run models.</summary>
    public const string ManagementKey = "sk-or-v1-test-management-0000000001";

    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    /// <summary>How many times the public model list was fetched.</summary>
    public int CatalogCalls => catalogCalls;

    /// <summary>When true, the model list answers 503.</summary>
    public bool CatalogDown { get; set; }

    private int catalogCalls;

    /// <summary>Answer for <c>response_format.json_schema.name</c>; tests set it.</summary>
    public ConcurrentDictionary<string, string> Answers { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorized = request.Headers.Authorization?.Parameter == ValidKey;
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/models", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
        {
            Interlocked.Increment(ref catalogCalls);
            return CatalogDown ? Error(HttpStatusCode.ServiceUnavailable, "down") : Json(HttpStatusCode.OK, Catalog);
        }

        if (path.EndsWith("/credits", StringComparison.Ordinal))
        {
            return request.Headers.Authorization?.Parameter == ManagementKey
                ? Json(HttpStatusCode.OK, """{"data":{"total_credits":20,"total_usage":12.58}}""")
                : Error(HttpStatusCode.Forbidden, "Management key required");
        }

        if (path.EndsWith("/key", StringComparison.Ordinal))
        {
            return authorized ? Json(HttpStatusCode.OK, """{"data":{"label":"test","usage":3.5,"usage_monthly":1.18,"limit":10,"limit_remaining":6.5,"limit_reset":"monthly","is_free_tier":false}}""") : Error(HttpStatusCode.Unauthorized, "No auth credentials found");
        }

        if (!authorized)
        {
            return Error(HttpStatusCode.Unauthorized, "No auth credentials found");
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Requests.Enqueue(body);

        if (body["model"]?.GetValue<string>() == BrokenModel)
        {
            return Error(HttpStatusCode.NotFound, "Model not found");
        }

        var schemaName = body["response_format"]?["json_schema"]?["name"]?.GetValue<string>();
        var content = schemaName is not null && Answers.TryGetValue(schemaName, out var answer) ? answer : "ok";
        var completion = new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 120, ["completion_tokens"] = 80 },
        };
        return Json(HttpStatusCode.OK, completion.ToJsonString());
    }

    // One of each case the catalog must handle: paid with and without a rating, free with an end date,
    // image output, JSON mode without schemas, a batch variant and a router.
    private const string Catalog = """
        {"data":[
          {"id":"vendor/paid-model","name":"Vendor: Paid","context_length":128000,"expiration_date":null,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["response_format","structured_outputs","max_tokens"],
           "pricing":{"prompt":"0.0000003","completion":"0.0000025"}},
          {"id":"vendor/top-model","name":"Vendor: Top","context_length":200000,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["structured_outputs"],
           "benchmarks":{"artificial_analysis":{"intelligence_index":61.4}},
           "pricing":{"prompt":"0.000003","completion":"0.000015"}},
          {"id":"vendor/top-model:batch","name":"Vendor: Top (batch)","context_length":200000,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["structured_outputs"],
           "benchmarks":{"artificial_analysis":{"intelligence_index":61.4}},
           "pricing":{"prompt":"0.0000015","completion":"0.0000075"}},
          {"id":"vendor/json-mode-model","name":"Vendor: JSON mode","context_length":8000,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["response_format"],
           "pricing":{"prompt":"0.000001","completion":"0.000001"}},
          {"id":"vendor/free-model:free","name":"Vendor: Free","context_length":32000,"expiration_date":"2026-12-31",
           "architecture":{"output_modalities":["text"]},"supported_parameters":["structured_outputs"],
           "pricing":{"prompt":"0","completion":"0"}},
          {"id":"vendor/image-model","name":"Vendor: Image","context_length":8000,
           "architecture":{"output_modalities":["image","text"]},"supported_parameters":["structured_outputs"],
           "pricing":{"prompt":"0.000001","completion":"0.000001"}},
          {"id":"vendor/plain-model","name":"Vendor: Plain","context_length":8000,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["max_tokens"],
           "pricing":{"prompt":"0.000001","completion":"0.000001"}},
          {"id":"openrouter/auto","name":"Auto Router","context_length":2000000,
           "architecture":{"output_modalities":["text"]},"supported_parameters":["structured_outputs"],
           "pricing":{"prompt":"-1","completion":"-1"}}
        ]}
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        Json(status, new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = message } }.ToJsonString());
}
