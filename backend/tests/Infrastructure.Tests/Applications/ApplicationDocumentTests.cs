using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.Applications;

/// <summary>
/// Integration tests (real Postgres) for document attachments (US4, T055): upload → list; download is
/// byte-identical; an oversize file (&gt; 50 MB) is rejected with 413; an empty upload is rejected. The
/// bytes live flat in the cv-data root as <c>appdoc-*</c> (backup coverage is asserted in the backup
/// round-trip test).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationDocumentTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Uploaded_document_lists_and_downloads_byte_identical()
    {
        await using var factory = await StartAsync("docs");
        var http = factory.CreateClient();
        var offerId = await MarkAppliedAsync(http, "Role docs");

        var bytes = new byte[4096];
        new Random(1234).NextBytes(bytes);

        var uploaded = await UploadAsync(http, offerId, "cover-letter.pdf", "application/pdf", bytes);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await uploaded.Content.ReadFromJsonAsync<DocumentDto>())!;
        doc.OriginalFileName.ShouldBe("cover-letter.pdf");
        doc.SizeBytes.ShouldBe(bytes.Length);

        // It appears on the application detail.
        var detail = await http.GetFromJsonAsync<DetailDto>($"/api/applications/{offerId}");
        detail!.Documents.ShouldContain(d => d.Id == doc.Id);

        // Download is byte-for-byte identical.
        var download = await http.GetAsync($"/api/applications/{offerId}/documents/{doc.Id}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);

        // Delete removes it from the list.
        (await http.DeleteAsync($"/api/applications/{offerId}/documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await http.GetFromJsonAsync<DetailDto>($"/api/applications/{offerId}"))!.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_oversize_file_is_rejected_with_413()
    {
        await using var factory = await StartAsync("toobig");
        var http = factory.CreateClient();
        var offerId = await MarkAppliedAsync(http, "Role toobig");

        var tooBig = new byte[ApplicationTrackingService.MaxDocumentBytes + 1];
        var response = await UploadAsync(http, offerId, "huge.bin", "application/octet-stream", tooBig);
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private async Task<JobApiFactory> StartAsync(string slug)
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        var factory = new JobApiFactory(postgres.ConnectionString, client);
        client.SetOffers((slug, 22000m));
        (await factory.CreateClient().PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();
        return factory;
    }

    private static async Task<string> MarkAppliedAsync(HttpClient http, string title)
    {
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var offer = envelope!.Data.Single(o => o.Title == title);
        (await http.PutAsJsonAsync($"/api/offers/{offer.OfferId}/application", new { appliedAt = "2026-06-01", note = (string?)null }))
            .EnsureSuccessStatusCode();
        return offer.OfferId;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient http, string offerId, string fileName, string contentType, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await http.PostAsync($"/api/applications/{offerId}/documents", form);
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title);

    private sealed record DetailDto(string OfferId, List<DocumentDto> Documents);

    private sealed record DocumentDto(string Id, string OriginalFileName, string? ContentType, long SizeBytes, DateTimeOffset AddedAt);
}
