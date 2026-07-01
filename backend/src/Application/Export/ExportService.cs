using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JobOfferMatcher.Application.Export;

/// <summary>
/// Produces a portable JSON or CSV export of all collected offers + statuses (FR-037 / SC-007,
/// Principle IX). Pure transformation of <see cref="IExportReader"/> rows — no EF/HTTP here.
/// </summary>
public sealed class ExportService(IExportReader reader)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The bytes are streamed as a file download (not embedded in HTML), so keep non-ASCII text
        // (e.g. "Kraków", "C++") human-readable rather than \uXXXX-escaped (FR-037 readability).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Leading characters a spreadsheet would evaluate as a formula (CSV injection, CWE-1236).</summary>
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    private static readonly string[] CsvHeader =
    [
        "offerId", "source", "title", "company", "location", "workMode", "employmentType",
        "seniority", "requiredSkills", "niceToHaveSkills", "salaryBands", "description", "affinityScore", "canonicalUrl",
        "availability", "userStatus", "applied", "appliedAt", "applicationNote", "roleGroupId",
        "firstSeenAt", "firstSuggestedAt", "lastSeenAt",
        "applicationStage", "applicationStatus", "applicationOutcome", "interviews",
    ];

    public async Task<ExportFile> ExportAsync(ExportFormat format, CancellationToken ct = default)
    {
        var rows = await reader.GetOffersAsync(ct);

        return format == ExportFormat.Csv
            ? new ExportFile("offers-export.csv", "text/csv", Encoding.UTF8.GetBytes(ToCsv(rows)))
            : new ExportFile(
                "offers-export.json",
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(rows, JsonOptions));
    }

    private static string ToCsv(IReadOnlyList<OfferExport> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', CsvHeader));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Field(r.OfferId.ToString()),
                Field(r.Source),
                Field(r.Title),
                Field(r.Company),
                Field(r.Location),
                Field(r.WorkMode),
                Field(r.EmploymentType),
                Field(r.Seniority),
                Field(string.Join("; ", r.RequiredSkills)),
                Field(string.Join("; ", r.NiceToHaveSkills)),
                Field(string.Join(" | ", r.SalaryBands.Select(FormatBand))),
                Field(r.Description),
                Field(r.AffinityScore?.ToString(CultureInfo.InvariantCulture)),
                Field(r.CanonicalUrl),
                Field(r.Availability),
                Field(r.UserStatus),
                Field(r.Applied ? "true" : "false"),
                Field(r.AppliedAt?.ToString("O", CultureInfo.InvariantCulture)),
                Field(r.ApplicationNote),
                Field(r.RoleGroupId?.ToString()),
                Field(r.FirstSeenAt.ToString("O", CultureInfo.InvariantCulture)),
                Field(r.FirstSuggestedAt.ToString("O", CultureInfo.InvariantCulture)),
                Field(r.LastSeenAt.ToString("O", CultureInfo.InvariantCulture)),
                Field(r.ApplicationStage),
                Field(r.ApplicationStatus),
                Field(r.ApplicationOutcome),
                Field(string.Join(" | ", r.Interviews.Select(FormatInterview))),
            }));
        }

        return sb.ToString();
    }

    private static string FormatBand(SalaryBandExport b)
    {
        var amount = (b.Min, b.Max) switch
        {
            (not null, not null) => $"{Num(b.Min)}-{Num(b.Max)}",
            (not null, null) => $"{Num(b.Min)}+",
            (null, not null) => $"up to {Num(b.Max)}",
            _ => "n/a",
        };

        return string.Join(' ', new[] { amount, b.Currency, b.Period, b.Basis, b.Tax }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string Num(decimal? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatInterview(InterviewEventExport i)
    {
        var parts = new List<string> { i.Kind };
        if (i.ScheduledAt is { } at)
        {
            parts.Add($"@ {at.ToString("O", CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(i.Outcome))
        {
            parts.Add($"→ {i.Outcome}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// RFC-4180 CSV escaping (quote + double embedded quotes), plus formula-injection neutralization
    /// (CWE-1236): source-supplied values starting with a formula trigger are prefixed with a single
    /// quote so a spreadsheet treats the cell as text instead of evaluating it.
    /// </summary>
    private static string Field(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && Array.IndexOf(FormulaTriggers, v[0]) >= 0)
        {
            v = "'" + v;
        }

        if (v.IndexOfAny(['"', ',', '\n', '\r']) < 0)
        {
            return v;
        }

        return $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
