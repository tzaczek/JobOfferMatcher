using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Sources;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Source management (FR-002/003) + role-group override (FR-016) — contracts/rest-api.md.</summary>
internal static class SourceEndpoints
{
    public sealed record SearchCriteriaDto(
        string[]? Categories,
        string[]? ExperienceLevels,
        string[]? EmploymentTypes,
        string[]? WorkingTimes,
        bool WithSalary,
        string? SortBy,
        string? OrderBy,
        string[]? WorkplaceKeep);

    public sealed record CreateSourceRequest(string Name, string Kind, SearchCriteriaDto SearchCriteria, bool RequiresLogin);

    public sealed record UpdateSourceRequest(string Name, SearchCriteriaDto SearchCriteria, bool RequiresLogin);

    public sealed record OverrideRequest(string Override);

    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/sources");

        group.MapGet("/", async (SourceService sources, CancellationToken ct) =>
        {
            var list = await sources.ListAsync(ct);
            return Results.Ok(new { data = list.Select(ToDto) });
        });

        group.MapPost("/", async (CreateSourceRequest body, SourceService sources, CancellationToken ct) =>
        {
            var kind = Enum.TryParse<SourceKind>(body.Kind, ignoreCase: true, out var k) ? k : SourceKind.DirectApi;
            var result = await sources.CreateAsync(body.Name, kind, ToSearch(body.SearchCriteria), body.RequiresLogin, ct);
            return result.ToHttp(s => Results.Ok(ToDto(s)));
        });

        group.MapPut("/{id}", async (string id, UpdateSourceRequest body, SourceService sources, CancellationToken ct) =>
        {
            if (!SourceId.TryParse(id, out var sourceId))
            {
                return Results.NotFound();
            }

            var result = await sources.UpdateAsync(sourceId, body.Name, ToSearch(body.SearchCriteria), body.RequiresLogin, ct);
            return result.ToHttp(s => Results.Ok(ToDto(s)));
        });

        group.MapPost("/{id}/enable", (string id, SourceService sources, CancellationToken ct) => SetEnabled(id, true, sources, ct));
        group.MapPost("/{id}/disable", (string id, SourceService sources, CancellationToken ct) => SetEnabled(id, false, sources, ct));

        api.MapPost("/role-groups/{id}/override", async (string id, OverrideRequest body, RoleGroupService groups, CancellationToken ct) =>
        {
            if (!RoleGroupId.TryParse(id, out var groupId))
            {
                return Results.NotFound();
            }

            var userOverride = string.Equals(body.Override, "notSame", StringComparison.OrdinalIgnoreCase)
                ? RoleGroupOverride.NotSame
                : RoleGroupOverride.Same;

            var result = await groups.SetOverrideAsync(groupId, userOverride, ct);
            return result.ToHttp(() => Results.NoContent());
        });

        return api;
    }

    private static async Task<IResult> SetEnabled(string id, bool enabled, SourceService sources, CancellationToken ct)
    {
        if (!SourceId.TryParse(id, out var sourceId))
        {
            return Results.NotFound();
        }

        var result = await sources.SetEnabledAsync(sourceId, enabled, ct);
        return result.ToHttp(() => Results.NoContent());
    }

    private static JobSourceSearch ToSearch(SearchCriteriaDto dto) => new()
    {
        Categories = dto.Categories ?? [],
        ExperienceLevels = dto.ExperienceLevels ?? [],
        EmploymentTypes = dto.EmploymentTypes ?? [],
        WorkingTimes = dto.WorkingTimes ?? [],
        WithSalary = dto.WithSalary,
        SortBy = dto.SortBy,
        OrderBy = dto.OrderBy,
        WorkplaceKeep = dto.WorkplaceKeep ?? [],
    };

    private static object ToDto(JobSource s) => new
    {
        id = s.Id.Value,
        name = s.Name,
        kind = s.Kind.ToString(),
        requiresLogin = s.RequiresLogin,
        enabled = s.Enabled,
        searchCriteria = new
        {
            categories = s.Search.Categories,
            experienceLevels = s.Search.ExperienceLevels,
            employmentTypes = s.Search.EmploymentTypes,
            workingTimes = s.Search.WorkingTimes,
            withSalary = s.Search.WithSalary,
            sortBy = s.Search.SortBy,
            orderBy = s.Search.OrderBy,
            workplaceKeep = s.Search.WorkplaceKeep,
        },
    };
}
