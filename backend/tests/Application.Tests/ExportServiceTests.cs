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

    private static OfferExport Row(string title = "Senior .NET Engineer", string company = "Acme") => new(
        Guid.NewGuid(), "justjoin.it", title, company, "Kraków", "Remote", "b2b", "senior",
        ["C#", ".NET"], [], [new SalaryBandExport(18000, 22000, "PLN", "monthly", "b2b", "net")],
        "https://justjoin.it/job/x", "available", "new", null,
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
}
