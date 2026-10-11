using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.Extensions.Logging;

namespace DrPodcast.Tests;

/// <summary>Model builders shared by the tests; callers override only the fields they care about.</summary>
internal static class TestData
{
    public static Series CreateSeries(
        string? presentationType = null,
        int numberOfSeries = 0,
        string? defaultOrder = null,
        string? title = "Test Series",
        int? numberOfSeasons = null) => new(
        Categories: null,
        NumberOfSeries: numberOfSeries,
        PresentationType: presentationType,
        LatestEpisodeStartTime: null,
        PresentationUrl: null,
        ExplicitContent: false,
        DefaultOrder: defaultOrder,
        Title: title,
        Punchline: null,
        Description: "Test description",
        ImageAssets: null,
        NumberOfSeasons: numberOfSeasons
    );

    public static Episode CreateEpisode(
        string title,
        int? seasonNumber = null,
        long? order = null,
        List<AudioAsset>? audioAssets = null,
        List<ImageAsset>? imageAssets = null,
        List<string>? categories = null) => new(
        Title: title,
        Description: null,
        PublishTime: "2024-01-01T00:00:00Z",
        StartTime: "2024-01-01T00:00:00Z",
        Id: $"ep-{title}",
        PresentationUrl: null,
        DurationMilliseconds: 60000,
        // An mp3 by default: episodes without a downloadable file are left out of feeds.
        AudioAssets: audioAssets ?? [new AudioAsset("mp3", 128, $"https://example.com/{Uri.EscapeDataString(title)}.mp3", 1000)],
        Categories: categories,
        ImageAssets: imageAssets,
        EpisodeNumber: null,
        SeasonNumber: seasonNumber,
        ExplicitContent: false,
        Order: order
    );
}

/// <summary>
/// In-memory stand-in for DR's series and episodes endpoints. Episodes are listed in the order given
/// unless a request asks for sort=desc, and <see cref="DropFromPage"/> reproduces DR's short pages.
/// </summary>
internal sealed class FakeDrApi : HttpMessageHandler
{
    private readonly Dictionary<string, (Series Series, List<Episode> Episodes)> _series = [];
    private readonly List<Uri> _requests = [];

    /// <summary>Series URNs that answer 503 Service Unavailable.</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>Applied before answering requests that match <see cref="DelayWhen"/> (all, if unset), honouring cancellation.</summary>
    public TimeSpan Delay { get; set; }

    public Func<Uri, bool>? DelayWhen { get; set; }

    /// <summary>Episode pages stop offering a next link once they reach this offset.</summary>
    public int? NextStopsAt { get; set; }

    /// <summary>Given (page size, episode), true leaves that episode out of the page.</summary>
    public Func<int, Episode, bool>? DropFromPage { get; set; }

    /// <summary>Requests for which this returns true answer 503 Service Unavailable.</summary>
    public Func<Uri, bool>? FailRequest { get; set; }

    public IReadOnlyList<Uri> Requests
    {
        get { lock (_requests) return [.. _requests]; }
    }

    public void Set(string urn, Series series, List<Episode> episodes)
    {
        lock (_series) _series[urn] = (series, episodes);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        lock (_requests) _requests.Add(uri);
        if (Delay > TimeSpan.Zero && DelayWhen?.Invoke(uri) != false)
            await Task.Delay(Delay, cancellationToken);

        // "{urn}" or "{urn}/episodes" after the API's base URL.
        var segments = uri.GetLeftPart(UriPartial.Path)[DrApiClient.ApiUrl.Length..].Split('/');
        var urn = segments[0];
        (Series Series, List<Episode> Episodes) entry;
        lock (_series)
        {
            if (Failing.Contains(urn) || FailRequest?.Invoke(uri) == true || !_series.TryGetValue(urn, out entry))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (segments is not [_, "episodes"])
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(entry.Series, PodcastJsonContext.Default.Series) };

        var query = HttpUtility.ParseQueryString(uri.Query);
        var limit = int.Parse(query["limit"] ?? "256", CultureInfo.InvariantCulture);
        var offset = int.Parse(query["offset"] ?? "0", CultureInfo.InvariantCulture);
        var ordered = query["sort"] == "desc"
            ? [.. entry.Episodes.OrderByDescending(e => e.StartTime, StringComparer.Ordinal)]
            : entry.Episodes;

        var items = ordered.Skip(offset).Take(limit).Where(e => DropFromPage?.Invoke(limit, e) != true).ToList();
        var next = offset + limit < Math.Min(ordered.Count, NextStopsAt ?? int.MaxValue) ? $"{DrApiClient.ApiUrl}{urn}/episodes?limit={limit}&offset={offset + limit}" : null;
        var page = new EpisodesPage(items, next, ordered.Count, offset);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page, PodcastJsonContext.Default.EpisodesPage) };
    }

    public static Series SeriesWith(string title, int latestEpisode, string defaultOrder = "Desc") =>
        TestData.CreateSeries(title: title, defaultOrder: defaultOrder) with { LatestEpisodeStartTime = StartTimeOf(latestEpisode) };

    /// <summary>Episode <paramref name="n"/> starts n days into 2026, with an mp3 asset of the given hash.</summary>
    public static Episode EpisodeNumber(int n, string? assetHash = null) =>
        TestData.CreateEpisode($"Episode {n}", order: n,
            audioAssets: [new AudioAsset("mp3", 192, $"https://api.dr.dk/radio/v1/assetlinks/urn:dr:radio:episode:{n:x4}/{assetHash ?? new string('a', 64)}", 1000)])
        with { Id = $"ep-{n}", StartTime = StartTimeOf(n), EpisodeNumber = n };

    public static List<Episode> Episodes(int count) => [.. Enumerable.Range(1, count).Select(n => EpisodeNumber(n))];

    public static string StartTimeOf(int n) =>
        new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc).AddDays(n).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler, TimeSpan? timeout = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (_entries) return [.. _entries]; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
    }
}
