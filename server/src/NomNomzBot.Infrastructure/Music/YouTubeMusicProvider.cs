// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// YouTube music provider. Search + resolve ride the app-level YouTube Data API v3 key
/// (<c>YouTube:ApiKey</c>) — there is NO per-user OAuth (music-sr.md §3.10 decision #8), so a channel
/// queues YouTube with zero YouTube connect. Playback rides the browser-source IFrame player by design
/// (music-sr.md §3.5.2): the SR fair queue is the source of truth and the overlay drives playback, so
/// the transport capabilities (Volume/Seek/Previous/Shuffle/Repeat/TransferDevice) are permanently
/// absent — the YouTube Data API has no playback-transport control — and consumers gate those members
/// off with <c>CAPABILITY_UNSUPPORTED</c>. Now-playing likewise comes from the IFrame relay over the
/// hub, not the Data API.
///
/// Live-reference notes (YouTube Data API v3, verified 2026-07-05):
/// - search.list: GET https://www.googleapis.com/youtube/v3/search?part=snippet&amp;type=video&amp;
///   videoEmbeddable=true&amp;maxResults={0-50}&amp;q=…&amp;key=… → items[].id.videoId, items[].snippet.
///   {title,channelTitle,liveBroadcastContent,thumbnails}. search.list alone lacks duration/embeddable/
///   age, so every hit is re-read through videos.list.
/// - videos.list: GET https://www.googleapis.com/youtube/v3/videos?part=snippet,contentDetails,status&amp;
///   id={csv}&amp;key=… → snippet.liveBroadcastContent ("none"|"live"|"upcoming"),
///   contentDetails.duration (ISO-8601), contentDetails.contentRating.ytRating (== "ytAgeRestricted"),
///   status.embeddable (bool).
/// </summary>
public sealed class YouTubeMusicProvider : IMusicProvider
{
    private const string ProviderName = "youtube";
    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";
    private const int MaxSearchResults = 50; // search.list maxResults hard cap (range 0–50).

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<YouTubeMusicProvider> _logger;

    public YouTubeMusicProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<YouTubeMusicProvider> logger
    )
    {
        _http = httpClientFactory.CreateClient("youtube");
        _apiKey = configuration["YouTube:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// The §3.5 routing set. The manage flags (<c>Library</c>/<c>Playlists</c>/<c>Subscriptions</c>)
    /// arrive with the YouTube-provider slice that wires videos.rate / playlists.* / subscriptions.*.
    /// </summary>
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.AcceptsSongRequests;

    /// <summary>The app-level Data API key is present, so search/resolve can reach YouTube.</summary>
    private bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PlayAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PauseAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.SkipAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        // No transport control on the YouTube Data API — capability permanently absent; consumers
        // never reach this member through the capability gate.
        _logger.LogDebug("YouTubeMusicProvider.PreviousAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(
        Guid broadcasterId,
        int volumePercent,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetVolumeAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SeekAsync(
        Guid broadcasterId,
        int positionSeconds,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SeekAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetShuffleAsync(
        Guid broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetShuffleAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetRepeatAsync(
        Guid broadcasterId,
        MusicRepeatMode mode,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetRepeatAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.GetDevicesAsync has no API-side transport");
        return Task.FromResult<IReadOnlyList<MusicDeviceInfo>>([]);
    }

    public Task TransferPlaybackAsync(
        Guid broadcasterId,
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.TransferPlaybackAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task<TrackInfo?> GetCurrentTrackAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // §3.5.2: YouTube now-playing is reported by the browser-source IFrame player relayed over the
        // OverlayHub, not by the Data API (which has no "currently playing" concept). Null here.
        return Task.FromResult<TrackInfo?>(null);
    }

    public async Task<IReadOnlyList<TrackInfo>> SearchAsync(
        Guid broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    )
    {
        // Unconfigured key or empty query ⇒ feature not available; degrade to empty, never throw.
        if (!IsConfigured || string.IsNullOrWhiteSpace(query))
            return [];

        int limit = Math.Clamp(maxResults, 1, MaxSearchResults);
        string searchUrl =
            $"{YouTubeApiBase}/search?part=snippet&type=video&videoEmbeddable=true"
            + $"&maxResults={limit}"
            + $"&q={Uri.EscapeDataString(query)}"
            + $"&key={Uri.EscapeDataString(_apiKey)}";

        YouTubeSearchResponse? search = await GetJsonAsync<YouTubeSearchResponse>(
            searchUrl,
            "search.list",
            cancellationToken
        );

        // search.list carries only ids + a thin snippet; the gates need duration/embeddable/age, which
        // only videos.list returns — so resolve the ordered ids there and preserve relevance ordering.
        List<string> orderedIds =
            search
                ?.Items?.Select(item => item.Id?.VideoId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList()
            ?? [];
        if (orderedIds.Count == 0)
            return [];

        Dictionary<string, YouTubeVideo> byId = await FetchVideosByIdAsync(
            orderedIds,
            cancellationToken
        );

        List<TrackInfo> results = [];
        foreach (string id in orderedIds)
        {
            if (!byId.TryGetValue(id, out YouTubeVideo? video))
                continue;

            // SR gates (§3.5/§3.5.2): a candidate the viewer could pick must actually be playable in the
            // browser-source player — exclude non-embeddable (can never render), age-restricted (YouTube
            // blocks embedded playback of these), and live/upcoming (no fixed track, not a song).
            if (!IsEmbeddable(video) || IsAgeRestricted(video) || !IsOnDemand(video.Snippet))
                continue;

            results.Add(MapToTrackInfo(video));
        }

        return results;
    }

    public async Task<TrackInfo?> ResolveTrackAsync(
        Guid broadcasterId,
        string uriOrId,
        CancellationToken cancellationToken = default
    )
    {
        // §3.5: null = not found/unavailable. Parse the id BEFORE any config/HTTP so garbage input fails
        // closed without a call; an unconfigured key likewise resolves to null with zero calls.
        string? videoId = ExtractVideoId(uriOrId);
        if (videoId is null || !IsConfigured)
            return null;

        string videosUrl =
            $"{YouTubeApiBase}/videos?part=snippet,contentDetails,status"
            + $"&id={videoId}"
            + $"&key={Uri.EscapeDataString(_apiKey)}";

        YouTubeVideoListResponse? response = await GetJsonAsync<YouTubeVideoListResponse>(
            videosUrl,
            "videos.list",
            cancellationToken
        );

        YouTubeVideo? video = response?.Items?.FirstOrDefault();
        if (video is null)
            return null; // unknown / private / deleted / region-blocked all return no items.

        // A live or upcoming broadcast is not a resolvable on-demand track.
        if (!IsOnDemand(video.Snippet))
            return null;

        // Unlike search, resolve keeps a found on-demand video and returns it WITH its gate flags
        // (embeddable/age/explicit) so the SR pipeline can reject with the precise reason (§3.5.2
        // failure taxonomy: not_embeddable / age_restricted), rather than a bare "not found".
        return MapToTrackInfo(video);
    }

    public Task<bool> AddToQueueAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        // §3.5.2: YouTube plays through our browser-source IFrame player, not a provider-side queue. The
        // SR fair queue (IMusicService) is the single source of truth and the overlay drives playback,
        // so there is nothing to push to a YouTube-side queue — the head-push is an accepting no-op.
        return Task.FromResult(true);
    }

    // ─── HTTP ────────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, YouTubeVideo>> FetchVideosByIdAsync(
        IReadOnlyList<string> orderedIds,
        CancellationToken cancellationToken
    )
    {
        string videosUrl =
            $"{YouTubeApiBase}/videos?part=snippet,contentDetails,status"
            + $"&id={string.Join(",", orderedIds)}"
            + $"&key={Uri.EscapeDataString(_apiKey)}";

        YouTubeVideoListResponse? response = await GetJsonAsync<YouTubeVideoListResponse>(
            videosUrl,
            "videos.list",
            cancellationToken
        );

        Dictionary<string, YouTubeVideo> byId = new(StringComparer.Ordinal);
        foreach (YouTubeVideo video in response?.Items ?? [])
        {
            if (video.Id is not null)
                byId[video.Id] = video;
        }

        return byId;
    }

    /// <summary>GETs and deserializes a Data API response; any transport/HTTP failure degrades to
    /// null (the caller yields empty/null). The URL carries the app key, so only the operation name is
    /// logged — never the URL.</summary>
    private async Task<T?> GetJsonAsync<T>(
        string url,
        string operation,
        CancellationToken cancellationToken
    )
        where T : class
    {
        try
        {
            HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "YouTube Data API {Operation} failed: {Status}",
                    operation,
                    response.StatusCode
                );
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "YouTube Data API {Operation} threw", operation);
            return null;
        }
    }

    // ─── Mapping / gates ──────────────────────────────────────────────────────

    private static TrackInfo MapToTrackInfo(YouTubeVideo video)
    {
        string videoId = video.Id ?? string.Empty;
        return new TrackInfo
        {
            TrackName = video.Snippet?.Title ?? string.Empty,
            Artist = video.Snippet?.ChannelTitle ?? string.Empty,
            Album = string.Empty, // YouTube has no album concept.
            TrackUri = WatchUrl(videoId),
            AlbumArtUrl = BestThumbnailUrl(video.Snippet?.Thumbnails),
            DurationMs = ParseDurationMs(video.ContentDetails?.Duration),
            Provider = ProviderName,
            ProviderTrackId = videoId,
            IsExplicit = false, // The Data API exposes no explicit-lyrics flag; age is the YouTube gate.
            IsAgeRestricted = IsAgeRestricted(video),
            IsEmbeddable = IsEmbeddable(video),
        };
    }

    private static bool IsEmbeddable(YouTubeVideo video) => video.Status?.Embeddable ?? false;

    private static bool IsAgeRestricted(YouTubeVideo video) =>
        string.Equals(
            video.ContentDetails?.ContentRating?.YtRating,
            "ytAgeRestricted",
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>A normal video-on-demand item — <c>liveBroadcastContent</c> is "none" (or absent);
    /// "live"/"upcoming" are broadcasts, not requestable tracks.</summary>
    private static bool IsOnDemand(YouTubeSnippet? snippet) =>
        snippet?.LiveBroadcastContent is null
        || string.Equals(snippet.LiveBroadcastContent, "none", StringComparison.OrdinalIgnoreCase);

    private static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static string? BestThumbnailUrl(YouTubeThumbnails? thumbnails) =>
        thumbnails?.High?.Url ?? thumbnails?.Medium?.Url ?? thumbnails?.Default?.Url;

    /// <summary>ISO-8601 duration (e.g. <c>PT4M13S</c>) → milliseconds; empty/unparseable ⇒ 0.</summary>
    private static int ParseDurationMs(string? iso8601Duration)
    {
        if (string.IsNullOrWhiteSpace(iso8601Duration))
            return 0;

        try
        {
            TimeSpan duration = System.Xml.XmlConvert.ToTimeSpan(iso8601Duration);
            return (int)duration.TotalMilliseconds;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Extracts an 11-char YouTube video id from a bare id or a watch/short URL: <c>youtube.com/watch?v=</c>
    /// (with extra <c>&amp;t=</c>/<c>&amp;list=</c> params), <c>youtu.be/</c>, <c>music.youtube.com</c>,
    /// <c>m.youtube.com</c>, and <c>/shorts/</c>|<c>/embed/</c>|<c>/v/</c> paths. Null if none is present.
    /// </summary>
    private static string? ExtractVideoId(string uriOrId)
    {
        if (string.IsNullOrWhiteSpace(uriOrId))
            return null;

        string value = uriOrId.Trim();

        if (IsVideoId(value))
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url))
            return null;

        string host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? url.Host[4..]
            : url.Host;

        // youtu.be/<id>
        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            string shortId = url.AbsolutePath.Trim('/');
            return IsVideoId(shortId) ? shortId : null;
        }

        bool isYouTubeHost =
            string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "music.youtube.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "m.youtube.com", StringComparison.OrdinalIgnoreCase);
        if (!isYouTubeHost)
            return null;

        // watch?v=<id> (ignores any other params like t=, list=, index=)
        string? watchId = QueryValue(url.Query, "v");
        if (watchId is not null && IsVideoId(watchId))
            return watchId;

        // /shorts/<id>, /embed/<id>, /v/<id>
        string[] segments = url.AbsolutePath.Trim('/').Split('/');
        if (
            segments.Length == 2
            && (
                string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[0], "v", StringComparison.OrdinalIgnoreCase)
            )
            && IsVideoId(segments[1])
        )
            return segments[1];

        return null;
    }

    private static bool IsVideoId(string value) =>
        value.Length == 11 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    /// <summary>Reads a single query-string value (leading '?' tolerated) without an ASP.NET dependency.</summary>
    private static string? QueryValue(string query, string key)
    {
        foreach (
            string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0)
                continue;

            if (string.Equals(pair[..equals], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return null;
    }

    // ─── YouTube Data API response models ─────────────────────────────────────

    private sealed class YouTubeSearchResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeSearchItem>? Items { get; set; }
    }

    private sealed class YouTubeSearchItem
    {
        [JsonPropertyName("id")]
        public YouTubeSearchId? Id { get; set; }
    }

    private sealed class YouTubeSearchId
    {
        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }
    }

    private sealed class YouTubeVideoListResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeVideo>? Items { get; set; }
    }

    private sealed class YouTubeVideo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public YouTubeSnippet? Snippet { get; set; }

        [JsonPropertyName("contentDetails")]
        public YouTubeContentDetails? ContentDetails { get; set; }

        [JsonPropertyName("status")]
        public YouTubeStatus? Status { get; set; }
    }

    private sealed class YouTubeSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

        [JsonPropertyName("liveBroadcastContent")]
        public string? LiveBroadcastContent { get; set; }

        [JsonPropertyName("thumbnails")]
        public YouTubeThumbnails? Thumbnails { get; set; }
    }

    private sealed class YouTubeThumbnails
    {
        [JsonPropertyName("default")]
        public YouTubeThumbnail? Default { get; set; }

        [JsonPropertyName("medium")]
        public YouTubeThumbnail? Medium { get; set; }

        [JsonPropertyName("high")]
        public YouTubeThumbnail? High { get; set; }
    }

    private sealed class YouTubeThumbnail
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class YouTubeContentDetails
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; set; }

        [JsonPropertyName("contentRating")]
        public YouTubeContentRating? ContentRating { get; set; }
    }

    private sealed class YouTubeContentRating
    {
        [JsonPropertyName("ytRating")]
        public string? YtRating { get; set; }
    }

    private sealed class YouTubeStatus
    {
        [JsonPropertyName("embeddable")]
        public bool Embeddable { get; set; }
    }
}
