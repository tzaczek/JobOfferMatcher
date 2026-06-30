using System.Net;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Drives the REAL Http clients through a capturing handler to lock the live-verified wire formats
/// (the rawSearch body + the pre-encoded "c%23;t" path) and the transport-status classification —
/// the one piece of source-specific logic the offline fixture/Fake clients bypass.
/// </summary>
public sealed class SourceClientWireFormatTests
{
    [Fact]
    public async Task NoFluffJobs_builds_the_verified_post_url_body_and_content_type()
    {
        var handler = new CapturingHandler(_ => Json("{\"postings\":[],\"totalPages\":1}"));
        var client = new HttpNoFluffJobsClient(new HttpClient(handler), Options.Create(new NoFluffJobsOptions()), NullLogger<HttpNoFluffJobsClient>.Instance);

        var (status, _) = await client.FetchListAsync(new JobSourceSearch { Categories = ["C#", ".NET"] }, 1, CancellationToken.None);

        status.ShouldBe(SourceFetchStatus.Ok);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery
            .ShouldBe("/api/search/posting?pageTo=1&pageSize=20&salaryCurrency=PLN&salaryPeriod=month&region=pl");
        handler.LastBody.ShouldBe("{\"rawSearch\":\"requirement=C#,.NET\"}");
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.ShouldBe("application/infiniteSearch+json");
    }

    [Fact]
    public async Task TheProtocol_builds_the_verified_get_url_without_double_encoding()
    {
        const string html = "<script id=\"__NEXT_DATA__\" type=\"application/json\">{\"props\":{\"pageProps\":{\"offersResponse\":{\"page\":{\"number\":1,\"size\":50,\"count\":1},\"offers\":[]}}}}</script>";
        var handler = new CapturingHandler(_ => Html(html));
        var client = new HttpTheProtocolClient(new HttpClient(handler), Options.Create(new TheProtocolOptions()), NullLogger<HttpTheProtocolClient>.Instance);

        var (status, _) = await client.FetchListAsync(new JobSourceSearch { Categories = ["C#"] }, 1, CancellationToken.None);

        status.ShouldBe(SourceFetchStatus.Ok);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        // "C#" → "c%23;t"; the already-encoded %23 must NOT become %2523.
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/filtry/c%23;t?sort=salary&pageNumber=1");
        handler.LastRequest.RequestUri.PathAndQuery.ShouldNotContain("%2523");
        handler.LastRequest.Headers.AcceptLanguage.ToString().ShouldBe("en");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, SourceFetchStatus.Blocked)]
    [InlineData(HttpStatusCode.TooManyRequests, SourceFetchStatus.Blocked)]
    [InlineData(HttpStatusCode.NotFound, SourceFetchStatus.Failed)]
    public async Task NoFluffJobs_classifies_http_status(HttpStatusCode code, SourceFetchStatus expected)
    {
        var client = new HttpNoFluffJobsClient(new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(code))),
            Options.Create(new NoFluffJobsOptions()), NullLogger<HttpNoFluffJobsClient>.Instance);

        var (status, _) = await client.FetchListAsync(new JobSourceSearch(), 1, CancellationToken.None);

        status.ShouldBe(expected);
    }

    [Fact]
    public async Task TheProtocol_missing_next_data_is_classified_blocked_not_a_layout_break()
    {
        var client = new HttpTheProtocolClient(new HttpClient(new CapturingHandler(_ => Html("<html>challenge interstitial</html>"))),
            Options.Create(new TheProtocolOptions()), NullLogger<HttpTheProtocolClient>.Instance);

        var (status, _) = await client.FetchListAsync(new JobSourceSearch(), 1, CancellationToken.None);

        status.ShouldBe(SourceFetchStatus.Blocked);
    }

    [Fact]
    public async Task A_request_timeout_is_classified_failed_not_propagated_as_cancellation()
    {
        // HttpClient timeout surfaces as TaskCanceledException with the CALLER token NOT signaled.
        var client = new HttpNoFluffJobsClient(
            new HttpClient(new CapturingHandler(_ => throw new TaskCanceledException("simulated timeout"))),
            Options.Create(new NoFluffJobsOptions()), NullLogger<HttpNoFluffJobsClient>.Instance);

        var (status, page) = await client.FetchListAsync(new JobSourceSearch(), 1, CancellationToken.None);

        status.ShouldBe(SourceFetchStatus.Failed); // a transport failure, never an aborted scan
        page.ShouldBeNull();
    }

    [Fact]
    public async Task Genuine_caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new HttpNoFluffJobsClient(
            new HttpClient(new CapturingHandler(_ => throw new TaskCanceledException())),
            Options.Create(new NoFluffJobsOptions()), NullLogger<HttpNoFluffJobsClient>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await client.FetchListAsync(new JobSourceSearch(), 1, cts.Token));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responder(request);
        }
    }
}
