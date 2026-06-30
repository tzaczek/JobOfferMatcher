using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

/// <summary>
/// Real transport for nofluffjobs.com: POSTs the <c>infiniteSearch</c> posting query built from the
/// saved search, paces ~1 req/s, retries 429/5xx with Polly backoff, and classifies 403/429 as
/// <see cref="SourceFetchStatus.Blocked"/> (research §1 / FR-007/FR-040). Sends a generic non-PII
/// User-Agent (Principle IV). The response is location-expanded; dedup happens in the source loop.
/// </summary>
public sealed class HttpNoFluffJobsClient : INoFluffJobsClient
{
    private readonly HttpClient _http;
    private readonly NoFluffJobsOptions _options;
    private readonly ILogger<HttpNoFluffJobsClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private bool _firstRequest = true;

    public HttpNoFluffJobsClient(HttpClient http, IOptions<NoFluffJobsOptions> options, ILogger<HttpNoFluffJobsClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_options.ApiBaseUrl);
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
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
        var url = BuildListUrl(page);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(BuildBody(search), Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_options.SearchContentType);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _pipeline.ExecuteAsync(async token => await _http.SendAsync(Clone(request), token), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation
        }
        catch (Exception ex)
        {
            // Anything else — including an HttpClient timeout (TaskCanceledException with ct NOT signaled) —
            // is a transport failure, not a scan-aborting cancellation (FR-040).
            _logger.LogWarning(ex, "nofluffjobs LIST request failed.");
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

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return (SourceFetchStatus.Ok, ParsePage(doc.RootElement));
    }

    internal static SourceListPage ParsePage(JsonElement root)
    {
        var items = new List<JsonElement>();
        if (root.TryGetProperty("postings", out var postings) && postings.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in postings.EnumerateArray())
            {
                items.Add(item.Clone()); // clone so elements outlive the JsonDocument
            }
        }

        var totalPages = 1;
        if (root.TryGetProperty("totalPages", out var tp) && tp.ValueKind == JsonValueKind.Number)
        {
            totalPages = tp.GetInt32();
        }

        return new SourceListPage(items, totalPages);
    }

    private string BuildBody(JobSourceSearch search)
    {
        var raw = search.Categories.Count > 0
            ? "requirement=" + string.Join(",", search.Categories)
            : _options.DefaultRawSearch;

        // Minimal body; criteria are carried by rawSearch which the API parses server-side.
        return JsonSerializer.Serialize(new { rawSearch = raw });
    }

    private string BuildListUrl(int page)
    {
        var sb = new StringBuilder(_options.ListPath);
        sb.Append('?');
        sb.Append("pageTo=").Append(page.ToString(CultureInfo.InvariantCulture));
        sb.Append("&pageSize=").Append(_options.PageSize.ToString(CultureInfo.InvariantCulture));
        sb.Append("&salaryCurrency=").Append(Uri.EscapeDataString(_options.SalaryCurrency));
        sb.Append("&salaryPeriod=").Append(Uri.EscapeDataString(_options.SalaryPeriod));
        sb.Append("&region=").Append(Uri.EscapeDataString(_options.Region));
        return sb.ToString();
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source)
    {
        // Polly retries re-send the request; an HttpRequestMessage may only be sent once, so clone it.
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        if (source.Content is StringContent original)
        {
            var body = original.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(body, Encoding.UTF8);
            clone.Content.Headers.ContentType = source.Content.Headers.ContentType;
        }

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

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
