namespace JobOfferMatcher.Domain.TailoredCvs;

/// <summary>A minimal, EF-free view of the offer the tailored-CV prompt is composed against (data-model §3).</summary>
public sealed record TailoredCvOfferView(string Title, string Company, string? Seniority);

/// <summary>
/// The pure, framework-free default-prompt composer (data-model §3). It produces the editable instruction
/// text from the offer (title/company/seniority), the emphasised skills (interpolated as a visible bullet
/// list so a skill-toggle changes the prompt — FR-004), the <c>cv_versions</c> two-column A4 layout
/// instruction (FR-007), and the explicit <b>no-fabrication</b> rule (FR-006). It never embeds the CV
/// text — the source CV is an attached, read-only input (R5). Deterministic, so the default prompt is
/// stable and unit-testable; the user's edits replace it wholesale before generation and whatever is
/// submitted is stored verbatim.
/// </summary>
public static class TailoredCvPrompt
{
    public static string BuildDefault(TailoredCvOfferView offer, IReadOnlyList<string> emphasisedSkills)
    {
        var seniority = string.IsNullOrWhiteSpace(offer.Seniority) ? "" : $" ({offer.Seniority.Trim()})";
        var emphasis = emphasisedSkills.Count > 0
            ? "Emphasise these areas the posting cares about, foregrounding my real experience with them:\n"
                + string.Join("\n", emphasisedSkills.Select(s => $"  - {s}"))
            : "Emphasise the skills and experience this posting cares about, foregrounding my real strengths.";

        return
            $"Tailor my CV for the role of {offer.Title} at {offer.Company}{seniority}.\n\n"
            + $"{emphasis}\n\n"
            + "Use ONLY information that is present in the attached source CV — re-emphasise, reorder, and "
            + "rephrase my real experience toward this role. NEVER invent or add an employer, job title, "
            + "date, qualification, certification, or skill that is not already in the attached CV. If the "
            + "posting wants something I do not have, do not claim it — foreground the closest real strength "
            + "instead.\n\n"
            + "Lay the CV out using the cv_versions two-column A4 layout (mirror v2_two_column.html and the "
            + "NOTES.md print/pagination rules): a navy left sidebar (contact + skills) and a white right "
            + "column (experience). Return ONE self-contained HTML document with inline CSS, sized to A4.";
    }
}
