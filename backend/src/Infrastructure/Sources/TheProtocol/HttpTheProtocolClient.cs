using System.Net;
using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Real transport for theprotocol.it: GETs the public <c>/filtry/&lt;path&gt;?pageNumber=N</c> listing
/// built from the saved search, paces ~1 req/s, retries 429/5xx with Polly backoff, and classifies
/// 403/429 as <see cref="SourceFetchStatus.Blocked"/> (research §1 / FR-007/FR-040). The offer list is
/// read from the page's <c>__NEXT_DATA__</c> JSON via <see cref="NextDataExtractor"/>. Sends a generic
/// non-PII User-Agent (Principle IV).
/// </summary>
public sealed class HttpTheProtocolClient : ITheProtocolClient
{
    private readonly HttpClient _http;
    private readonly TheProtocolOptions _options;
    private readonly ILogger<HttpTheProtocolClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private bool _firstRequest = true;

    public HttpTheProtocolClient(HttpClient http, IOptions<TheProtocolOptions> options, ILogger<HttpTheProtocolClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_options.SiteBaseUrl);
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
        }

        if (!_http.DefaultRequestHeaders.Contains("Accept-Language"))
        {
            _http.DefaultRequestHeaders.Add("Accept-Language", _options.AcceptLanguage);
        }

        if (!_http.DefaultRequestHeaders.Contains("Accept"))
        {
            _http.DefaultRequestHeaders.Add("Accept", _options.Accept);
        }

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
                    .Handle<HttpRequestException>(),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
            })
            .Build();
    }

    public async Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(
        JobSourceSearch search, int page, CancellationToken ct)
    {
        await PaceAsync(ct);
        var url = BuildListUrl(search, page);

        HttpResponseMessage response;
        try
        {
            response = await _pipeline.ExecuteAsync(async token => await _http.GetAsync(url, token), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation
        }
        catch (Exception ex)
        {
            // Anything else — including an HttpClient timeout (TaskCanceledException with ct NOT signaled) —
            // is a transport failure, not a scan-aborting cancellation (FR-040).
            _logger.LogWarning(ex, "theprotocol LIST request failed.");
            return (SourceFetchStatus.Failed, null);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return (SourceFetchStatus.Blocked, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (SourceFetchStatus.Failed, null);
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        if (NextDataExtractor.ExtractOffersResponse(html) is not { } offersResponse)
        {
            // No embedded offer list usually means an anti-bot interstitial rather than a real layout break.
            _logger.LogWarning("theprotocol response carried no __NEXT_DATA__ offers (possible challenge) at page {Page}.", page);
            return (SourceFetchStatus.Blocked, null);
        }

        return (SourceFetchStatus.Ok, ParsePage(offersResponse));
    }

    internal static SourceListPage ParsePage(JsonElement offersResponse)
    {
        var items = new List<JsonElement>();
        if (offersResponse.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in offers.EnumerateArray())
            {
                items.Add(offer.Clone());
            }
        }

        var totalPages = 1;
        if (offersResponse.TryGetProperty("page", out var pageMeta) && pageMeta.ValueKind == JsonValueKind.Object
            && pageMeta.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number)
        {
            totalPages = count.GetInt32();
        }

        return new SourceListPage(items, totalPages);
    }

    private string BuildListUrl(JobSourceSearch search, int page) =>
        TheProtocolRequest.BuildListPathAndQuery(_options, search, page);

    private async Task PaceAsync(CancellationToken ct)
    {
        if (_firstRequest)
        {
            _firstRequest = false;
            return;
        }

        if (_options.RequestDelayMs > 0)
        {
            await Task.Delay(_options.RequestDelayMs, ct);
        }
    }
}
