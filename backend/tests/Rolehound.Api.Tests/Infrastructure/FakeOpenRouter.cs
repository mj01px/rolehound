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

    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    /// <summary>Answer for <c>response_format.json_schema.name</c>; tests set it.</summary>
    public ConcurrentDictionary<string, string> Answers { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorized = request.Headers.Authorization?.Parameter == ValidKey;
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/key", StringComparison.Ordinal))
        {
            return authorized ? Json(HttpStatusCode.OK, """{"data":{"label":"test","usage":0,"limit":null,"is_free_tier":false}}""") : Error(HttpStatusCode.Unauthorized, "No auth credentials found");
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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        Json(status, new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = message } }.ToJsonString());
}
