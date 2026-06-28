using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Sources;

/// <summary>
/// A configured place offers are collected from (data-model §JobSource). Its
/// <see cref="Search"/> criteria are user-editable (FR-002) and it can be enabled/disabled
/// (FR-003) without code changes. Mutated only through its methods (aggregate root).
/// </summary>
public sealed class JobSource
{
    public SourceId Id { get; private set; }
    public string Name { get; private set; }
    public SourceKind Kind { get; private set; }
    public JobSourceSearch Search { get; private set; }
    public bool RequiresLogin { get; private set; }
    public bool Enabled { get; private set; }

    private JobSource()
    {
        // EF Core materialization.
        Name = string.Empty;
        Search = new JobSourceSearch();
    }

    private JobSource(
        SourceId id,
        string name,
        SourceKind kind,
        JobSourceSearch search,
        bool requiresLogin,
        bool enabled)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Search = search;
        RequiresLogin = requiresLogin;
        Enabled = enabled;
    }

    public static Result<JobSource> Create(
        SourceId id,
        string name,
        SourceKind kind,
        JobSourceSearch search,
        bool requiresLogin = false,
        bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error("InvalidSourceName", "Source name is required.");
        }

        return new JobSource(id, name.Trim(), kind, search, requiresLogin, enabled);
    }

    public Result UpdateSearch(JobSourceSearch search)
    {
        Search = Guard.AgainstNull(search);
        return Result.Success();
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error("InvalidSourceName", "Source name is required.");
        }

        Name = name.Trim();
        return Result.Success();
    }

    public void SetRequiresLogin(bool requiresLogin) => RequiresLogin = requiresLogin;

    public void Enable() => Enabled = true;

    public void Disable() => Enabled = false;
}
