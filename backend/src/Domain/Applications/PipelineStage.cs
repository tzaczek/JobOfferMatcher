using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// One user-configurable column of the interview pipeline (data-model §3). The ordered set of stages
/// IS the pipeline (FR-019); an application always references an existing stage. Name/position are
/// validated in the entity (Principle III).
/// </summary>
public sealed class PipelineStage
{
    /// <summary>Longest stage name the Domain accepts (rejected past this — Principle III).</summary>
    public const int MaxNameLength = 80;

    public PipelineStageId Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Position { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PipelineStage()
    {
        // EF Core materialization.
    }

    public static Result<PipelineStage> Create(string name, int position, DateTimeOffset now)
    {
        var validated = Validate(name);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        return new PipelineStage
        {
            Id = PipelineStageId.New(),
            Name = validated.Value,
            Position = position,
            CreatedAt = now,
        };
    }

    public Result Rename(string name)
    {
        var validated = Validate(name);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        Name = validated.Value;
        return Result.Success();
    }

    public void MoveTo(int position) => Position = position;

    private static Result<string> Validate(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (trimmed.Length == 0)
        {
            return new Error("StageNameRequired", "A pipeline stage name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            return new Error("StageNameTooLong", $"A stage name cannot exceed {MaxNameLength} characters.");
        }

        return trimmed;
    }
}
