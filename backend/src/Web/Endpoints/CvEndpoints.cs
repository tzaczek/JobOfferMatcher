using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>CV upload + derived profile (contracts/rest-api.md §CV, FR-021/022/026).</summary>
internal static class CvEndpoints
{
    public sealed record UpdateProfileRequest(
        decimal? SalaryFloor,
        decimal? SalaryTarget,
        string[]? PreferredWorkModes,
        string[]? PreferredEmployment);

    public static IEndpointRouteBuilder MapCvEndpoints(this IEndpointRouteBuilder api)
    {
        var cvGroup = api.MapGroup("/cv");

        cvGroup.MapGet("/", async (CvService cv, CancellationToken ct) =>
        {
            var list = await cv.ListAsync(ct);
            return Results.Ok(new { data = list.Select(ToCvDto) });
        });

        cvGroup.MapPost("/", async (IFormFile? file, CvService cv, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = new { code = "NoFile", message = "A PDF file is required." } });
            }

            await using var stream = file.OpenReadStream();
            var created = await cv.UploadAsync(file.FileName, stream, ct);
            return Results.Ok(ToCvDto(created)); // 200 even when unreadable → graceful degradation
        }).DisableAntiforgery();

        cvGroup.MapDelete("/{id}", async (string id, CvService cv, CancellationToken ct) =>
        {
            if (!CvId.TryParse(id, out var cvId))
            {
                return Results.NotFound();
            }

            var result = await cv.DeleteAsync(cvId, ct);
            return result.ToHttp(() => Results.NoContent());
        });

        var profileGroup = api.MapGroup("/profile");

        profileGroup.MapGet("/", async (ProfileService profile, CancellationToken ct) =>
            Results.Ok(await profile.GetAsync(ct)));

        profileGroup.MapPut("/", async (UpdateProfileRequest body, ProfileService profile, CancellationToken ct) =>
        {
            var preferences = new ProfilePreferences
            {
                SalaryFloor = body.SalaryFloor,
                SalaryTarget = body.SalaryTarget,
                PreferredWorkModes = body.PreferredWorkModes ?? [],
                PreferredEmployment = ParseEmployment(body.PreferredEmployment),
            };
            await profile.UpdatePreferencesAsync(preferences, ct);
            return Results.Ok(await profile.GetAsync(ct));
        });

        return api;
    }

    private static IReadOnlyList<EmploymentBasis> ParseEmployment(string[]? values) =>
        (values ?? [])
            .Select(v => Enum.TryParse<EmploymentBasis>(v, ignoreCase: true, out var b) ? b : EmploymentBasis.Unknown)
            .Where(b => b != EmploymentBasis.Unknown)
            .Distinct()
            .ToList();

    private static object ToCvDto(CandidateCv cv) => new
    {
        id = cv.Id.Value,
        fileName = cv.FileName,
        isReadable = cv.IsReadable,
        extractedAt = cv.ExtractedAt,
        skills = cv.DerivedProfile?.Skills.Select(s => s.DisplayName) ?? [],
        seniority = cv.DerivedProfile?.Seniority.ToString(),
    };
}
