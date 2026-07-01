using JobOfferMatcher.Domain.Applications;

namespace JobOfferMatcher.Domain.Tests.Applications;

/// <summary>
/// <see cref="PipelineStage"/> unit tests (T027): name validation, rename, and <see cref="PipelineStage.MoveTo"/>
/// ordering.
/// </summary>
public sealed class PipelineStageTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_the_name_and_sets_position()
    {
        var result = PipelineStage.Create("  Screening  ", 2, At);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Screening");
        result.Value.Position.ShouldBe(2);
    }

    [Fact]
    public void Create_rejects_a_blank_name()
    {
        var result = PipelineStage.Create("   ", 0, At);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("StageNameRequired");
    }

    [Fact]
    public void Create_rejects_an_overlong_name()
    {
        var result = PipelineStage.Create(new string('x', PipelineStage.MaxNameLength + 1), 0, At);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("StageNameTooLong");
    }

    [Fact]
    public void Rename_changes_the_name()
    {
        var stage = PipelineStage.Create("Applied", 0, At).Value;

        var result = stage.Rename("Submitted");

        result.IsSuccess.ShouldBeTrue();
        stage.Name.ShouldBe("Submitted");
    }

    [Fact]
    public void Rename_rejects_a_blank_name()
    {
        var stage = PipelineStage.Create("Applied", 0, At).Value;

        var result = stage.Rename("");

        result.IsFailure.ShouldBeTrue();
        stage.Name.ShouldBe("Applied"); // unchanged
    }

    [Fact]
    public void MoveTo_changes_the_position()
    {
        var stage = PipelineStage.Create("Offer", 3, At).Value;

        stage.MoveTo(1);

        stage.Position.ShouldBe(1);
    }
}
