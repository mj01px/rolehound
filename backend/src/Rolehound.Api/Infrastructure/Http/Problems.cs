using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Rolehound.Api.Infrastructure.Http;

/// <summary>
/// The only way endpoints report errors: RFC 9457 problem details with a
/// stable machine-readable <c>code</c> and a message in Portuguese for people.
/// </summary>
public static class Problems
{
    public static ProblemHttpResult Unauthorized(string code, string detail) => Problem(StatusCodes.Status401Unauthorized, code, detail);

    public static ProblemHttpResult Forbidden(string code, string detail) => Problem(StatusCodes.Status403Forbidden, code, detail);

    public static ProblemHttpResult NotFound(string detail = "Não encontrado.") => Problem(StatusCodes.Status404NotFound, "not_found", detail);

    public static ProblemHttpResult Conflict(string code, string detail) => Problem(StatusCodes.Status409Conflict, code, detail);

    public static ProblemHttpResult BadRequest(string code, string detail) => Problem(StatusCodes.Status400BadRequest, code, detail);

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, detail: "Revise os campos.", extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    public static ValidationProblem Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Maps Identity errors to one field, keeping their Portuguese descriptions.</summary>
    public static ValidationProblem Validation(string field, IdentityResult result) =>
        Validation(new Dictionary<string, string[]> { [field] = result.Errors.Select(e => e.Description).ToArray() });

    public static ProblemHttpResult Problem(int status, string code, string detail) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
