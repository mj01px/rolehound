using System.ComponentModel.DataAnnotations;

namespace Rolehound.Api.Infrastructure;

/// <summary>Where the web app lives: used for links in emails and for CORS.</summary>
public sealed class WebOptions
{
    public const string Section = "Web";

    [Required, Url]
    public string BaseUrl { get; init; } = "http://localhost:5173";

    public Uri Link(string path, string token) =>
        // The token travels in the fragment, so it never reaches server logs or Referer headers.
        new($"{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}#token={Uri.EscapeDataString(token)}");
}
