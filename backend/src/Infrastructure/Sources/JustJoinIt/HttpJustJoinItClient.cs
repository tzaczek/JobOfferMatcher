using System.Net;
using System.Text;
using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

/// <summary>
/// Real transport for justjoin.it: builds the LIST query from the saved search, paces ~1 req/s,
/// retries 429/5xx with Polly backoff, and classifies 403/anti-bot as <see cref="FetchStatus.Blocked"/>
/// (research §1 / FR-007/FR-040). Sends a generic non-PII User-Agent (Principle IV).
/// </summary>
public sealed class HttpJustJoinItClient : IJustJoinItClient
{
    private readonly HttpClient _http;
    private readonly JustJoinItOptions _options;
    private readonly ILogger<HttpJustJoinItClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private bool _firstRequest = true;

    public HttpJustJoinItClient(HttpClient http, IOptions<JustJoinItOptions> options, ILogger<HttpJustJoinItClient> logger)
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

    public async Task<(FetchStatus Status, JustJoinItPage? Page)> FetchListAsync(
        JobSourceSearch search, int from, CancellationToken ct)
    {
        await PaceAsync(ct);
        var url = BuildListUrl(search, from);

        HttpResponseMessage response;
        try
        {
            response = await _pipeline.ExecuteAsync(
                async token => await _http.GetAsync(url, token), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "justjoin.it LIST request failed.");
            return (FetchStatus.Failed, null);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return (FetchStatus.Blocked, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (FetchStatus.Failed, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return (FetchStatus.Ok, ParsePage(doc.RootElement));
    }

    public async Task<JsonElement?> FetchDetailAsync(string slug, CancellationToken ct)
    {
        await PaceAsync(ct);
        var url = _options.DetailPath.Replace("{slug}", Uri.EscapeDataString(slug), StringComparison.Ordinal);

        try
        {
            var response = await _pipeline.ExecuteAsync(async token => await _http.GetAsync(url, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "justjoin.it DETAIL request for {Slug} failed.", slug);
            return null;
        }
    }

    private static JustJoinItPage ParsePage(JsonElement root)
    {
        var items = new List<JsonElement>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                items.Add(item.Clone()); // clone so elements outlive the JsonDocument
            }
        }

        long total = 0;
        int? nextCursor = null;
        if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            if (meta.TryGetProperty("totalItems", out var t) && t.ValueKind == JsonValueKind.Number)
            {
                total = t.GetInt64();
            }

            if (meta.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.Object
                && next.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.Number)
            {
                nextCursor = cursor.GetInt32();
            }
        }

        return new JustJoinItPage(items, total, nextCursor);
    }

    private string BuildListUrl(JobSourceSearch search, int from)
    {
        var sb = new StringBuilder(_options.ListPath);
        sb.Append('?');
        var first = true;

        void Add(string key, string value)
        {
            if (!first)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            first = false;
        }

        foreach (var c in search.Categories) Add("categories[]", c);
        foreach (var e in search.ExperienceLevels) Add("experienceLevels[]", e);
        foreach (var e in search.EmploymentTypes) Add("employmentTypes[]", e);
        foreach (var w in search.WorkingTimes) Add("workingTimes[]", w);
        if (search.WithSalary) Add("withSalary", "true");
        if (!string.IsNullOrWhiteSpace(search.SortBy)) Add("sortBy", search.SortBy);
        if (!string.IsNullOrWhiteSpace(search.OrderBy)) Add("orderBy", search.OrderBy);
        Add("from", from.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return sb.ToString();
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
