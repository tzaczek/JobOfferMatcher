using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Web.Infrastructure;

/// <summary>
/// Maps Domain <see cref="Result"/>/<see cref="Result{T}"/> failures to HTTP problem responses
/// at the Web boundary (contracts/rest-api.md): expected failures become 4xx with a
/// <c>{ error: { code, message } }</c> body — never thrown exceptions.
/// </summary>
public static class ResultExtensions
{
    // Known expected-failure codes that map to a specific status; everything else is 400.
    private static int StatusFor(string code) => code switch
    {
        "ScanInProgress" => StatusCodes.Status409Conflict,
        "Reentrancy" => StatusCodes.Status409Conflict,
        "BusyMaintenance" => StatusCodes.Status409Conflict,
        "NotFound" => StatusCodes.Status404NotFound,
        "OfferNotFound" => StatusCodes.Status404NotFound,
        "SourceNotFound" => StatusCodes.Status404NotFound,
        "RoleGroupNotFound" => StatusCodes.Status404NotFound,
        "CvNotFound" => StatusCodes.Status404NotFound,
        // Backup/restore (003): a backup created by a newer app can't be represented by this build.
        "IncompatibleNewer" => StatusCodes.Status422UnprocessableEntity,
        // Mid-flight failures after validation — live data was rolled back / no partial file sent.
        "BackupFailed" => StatusCodes.Status500InternalServerError,
        "RestoreFailed" => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    public static IResult ToProblem(this Error error) =>
        Results.Json(
            new { error = new { code = error.Code, message = error.Message } },
            statusCode: StatusFor(error.Code));

    public static IResult ToHttp(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.Match(onSuccess, error => error.ToProblem());
}
