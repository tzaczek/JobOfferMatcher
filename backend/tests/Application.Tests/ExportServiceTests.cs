using System.Text;
using JobOfferMatcher.Application.Export;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Unit tests (T066/T068): the export is human-readable JSON (non-ASCII kept verbatim) and
/// CSV-injection-safe (CWE-1236 — source-supplied formula triggers are neutralized).
/// </summary>
public sealed class ExportServiceTests
{
    private sealed class FakeReader(IReadOnlyList<OfferExport> rows) : IExportReader
    {
        public Task<IReadOnlyList<OfferExport>> GetOffersAsync(CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    private static OfferExport Row(
        string title = "Senior .NET Engineer",
        string company = "Acme",
        bool applied = false,
        DateTimeOffset? appliedAt = null,
        string? note = null) => new(
        Guid.NewGuid(), "justjoin.it", title, company, "Kraków", "Remote", "b2b", "senior",
        ["C#", ".NET"], [], [new SalaryBandExport(18000, 22000, "PLN", "monthly", "b2b", "net")],
        "https://justjoin.it/job/x", "available", "new", applied, appliedAt, note, null,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ExportService Service(params OfferExport[] rows) => new(new FakeReader(rows));

    [Fact]
    public async Task Json_export_keeps_non_ascii_text_readable()
    {
        var file = await Service(Row(title: "C++ Developer", company: "Kraków Software")).ExportAsync(ExportFormat.Json);
        var json = Encoding.UTF8.GetString(file.Content);

        file.ContentType.ShouldBe("application/json");
        json.ShouldContain("C++ Developer"); // not C++
        json.ShouldContain("Kraków Software"); // not Kraków
        json.ShouldNotContain("\\u");
    }

    [Fact]
    public async Task Csv_export_neutralizes_formula_injection()
    {
        var file = await Service(Row(company: "=2+5")).ExportAsync(ExportFormat.Csv);
        var csv = Encoding.UTF8.GetString(file.Content);

        file.ContentType.ShouldBe("text/csv");
        csv.ShouldContain("'=2+5"); // apostrophe-prefixed → spreadsheet treats as text
        csv.ShouldNotContain(",=2+5"); // never emitted as a bare formula cell
    }

    [Fact]
    public async Task Csv_export_quotes_fields_containing_commas()
    {
        var csv = Encoding.UTF8.GetString((await Service(Row(title: "Engineer, Senior")).ExportAsync(ExportFormat.Csv)).Content);

        csv.ShouldContain("\"Engineer, Senior\"");
    }

    [Fact]
    public async Task Export_includes_the_applied_flag_date_and_note()
    {
        var when = new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
        var row = Row(applied: true, appliedAt: when, note: "referred by Anna");

        var csv = Encoding.UTF8.GetString((await Service(row).ExportAsync(ExportFormat.Csv)).Content);
        csv.ShouldContain("applied,appliedAt,applicationNote"); // header columns present
        csv.ShouldContain("true");
        csv.ShouldContain("referred by Anna");

        var json = Encoding.UTF8.GetString((await Service(row).ExportAsync(ExportFormat.Json)).Content);
        json.ShouldContain("\"applied\": true");
        json.ShouldContain("referred by Anna");

        // A not-applied row serializes the flag as false with null date/note (default Row()).
        var notAppliedJson = Encoding.UTF8.GetString((await Service(Row()).ExportAsync(ExportFormat.Json)).Content);
        notAppliedJson.ShouldContain("\"applied\": false");
        notAppliedJson.ShouldContain("\"appliedAt\": null");
        notAppliedJson.ShouldContain("\"applicationNote\": null");
    }
}
