using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Salary-normalization + scoring-weights settings (contracts/rest-api.md §Settings).</summary>
internal static class SettingsEndpoints
{
    public sealed record NormalizationRequest(
        string BaseCurrency,
        Dictionary<string, decimal> FxToBase,
        decimal AssumedMonthlyHours,
        decimal AssumedMonthlyWorkingDays,
        decimal B2bToPermanentFactor,
        string RangeStrategy,
        string FxSource);

    public sealed record WeightsRequest(int Skills, int Seniority, int WorkMode, int Employment, int Salary);

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings");

        group.MapGet("/normalization", async (SettingsService settings, CancellationToken ct) =>
        {
            var s = (await settings.GetAsync(ct)).Normalization;
            return Results.Ok(ToNormalizationDto(s));
        });

        group.MapPut("/normalization", async (NormalizationRequest body, SettingsService settings, CancellationToken ct) =>
        {
            var baseCurrency = Currency.Create(body.BaseCurrency);
            if (baseCurrency.IsFailure)
            {
                return baseCurrency.Error.ToProblem();
            }

            var normalization = new SalaryNormalizationSettings
            {
                BaseCurrency = baseCurrency.Value,
                FxToBase = body.FxToBase,
                AssumedMonthlyHours = body.AssumedMonthlyHours,
                AssumedMonthlyWorkingDays = body.AssumedMonthlyWorkingDays,
                B2bToPermanentFactor = body.B2bToPermanentFactor,
                RangeStrategy = Enum.TryParse<RangePointStrategy>(body.RangeStrategy, ignoreCase: true, out var rs) ? rs : RangePointStrategy.Midpoint,
                FxSource = body.FxSource,
            };

            await settings.UpdateNormalizationAsync(normalization, ct);
            return Results.Ok(ToNormalizationDto(normalization));
        });

        group.MapGet("/weights", async (SettingsService settings, CancellationToken ct) =>
        {
            var w = (await settings.GetAsync(ct)).Weights;
            return Results.Ok(ToWeightsDto(w));
        });

        group.MapPut("/weights", async (WeightsRequest body, SettingsService settings, CancellationToken ct) =>
        {
            var weights = new ScoringWeights(body.Skills, body.Seniority, body.WorkMode, body.Employment, body.Salary);
            var result = await settings.UpdateWeightsAsync(weights, ct);
            return result.ToHttp(() => Results.Ok(ToWeightsDto(weights)));
        });

        return api;
    }

    private static object ToNormalizationDto(SalaryNormalizationSettings s) => new
    {
        baseCurrency = s.BaseCurrency.Code,
        fxToBase = s.FxToBase,
        assumedMonthlyHours = s.AssumedMonthlyHours,
        assumedMonthlyWorkingDays = s.AssumedMonthlyWorkingDays,
        b2bToPermanentFactor = s.B2bToPermanentFactor,
        rangeStrategy = s.RangeStrategy.ToString(),
        fxAsOf = s.FxAsOf.ToString("yyyy-MM-dd"),
        fxSource = s.FxSource,
    };

    private static object ToWeightsDto(ScoringWeights w) => new
    {
        skills = w.Skills,
        seniority = w.Seniority,
        workMode = w.WorkMode,
        employment = w.Employment,
        salary = w.Salary,
        total = w.Total,
    };
}
