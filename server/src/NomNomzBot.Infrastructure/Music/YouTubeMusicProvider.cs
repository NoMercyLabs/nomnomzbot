// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// YouTube music provider. Two distinct auth planes (music-sr.md §3.10 AUTH STANCE):
/// <list type="bullet">
/// <item><b>Search + resolve</b> ride the app-level YouTube Data API v3 key (<c>YouTube:ApiKey</c>) —
/// no per-user OAuth, so a channel queues YouTube with zero YouTube connect.</item>
/// <item><b>Per-user manage</b> (<see cref="IMusicProviderManageApi"/> — videos.rate / playlists.* /
/// playlistItems.* / subscriptions.*) rides the broadcaster's own <c>youtube.manage</c> OAuth token from
/// the vault (Service Name="youtube"); an unconnected/unscoped channel fails <c>MISSING_SCOPE</c>. The
/// OAuth bearer, NOT the app key, authorizes these calls (the app key is search-only).</item>
/// </list>
/// Playback rides the browser-source IFrame player by design (music-sr.md §3.5.2): the SR fair queue is
/// the source of truth and the overlay drives playback, so the transport capabilities
/// (Volume/Seek/Previous/Shuffle/Repeat/TransferDevice) are permanently absent — the YouTube Data API has
/// no playback-transport control — and consumers gate those members off with <c>CAPABILITY_UNSUPPORTED</c>.
/// Now-playing likewise comes from the IFrame relay over the hub, not the Data API.
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
/// - Manage (OAuth bearer, scope youtube / youtube.force-ssl): videos.rate (POST /videos/rate?id=&amp;
///   rating=like|dislike|none → 204); videos.getRating (GET /videos/getRating?id={csv} → items[].
///   {videoId,rating}); saved list = videos.list?myRating=like (OAuth); playlists insert/update(PUT)/
///   delete(?id=)/list(?mine=true) (part=snippet,status); playlistItems insert (snippet.resourceId
///   kind=youtube#video) / list(?playlistId=&amp;videoId=) → id → delete(?id={playlistItemId});
///   subscriptions insert (snippet.resourceId kind=youtube#channel) / list(?mine=true[&amp;forChannelId=])
///   → id → delete(?id={subscriptionId}).
/// </summary>
public sealed class YouTubeMusicProvider : IMusicProvider, IMusicProviderManageApi
{
    private const string ProviderName = "youtube";
    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";
    private const int MaxSearchResults = 50; // search.list maxResults hard cap (range 0–50).
    private const int RatingIdsPerRequest = 50; // videos.getRating id cap per live reference.

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IYouTubeAccessTokenProvider _accessTokens;
    private readonly ILogger<YouTubeMusicProvider> _logger;

    public YouTubeMusicProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IYouTubeAccessTokenProvider accessTokens,
        ILogger<YouTubeMusicProvider> logger
    )
    {
        _http = httpClientFactory.CreateClient("youtube");
        _apiKey = configuration["YouTube:ApiKey"] ?? string.Empty;
        _accessTokens = accessTokens;
        _logger = logger;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// The §3.5/§3.10 YouTube set (music-sr.md line 325): search-fed queue + the per-user manage surface.
    /// <c>Library</c> = videos.rate (like/dislike) + the liked-videos read; <c>Playlists</c> = playlists.*
    /// + playlistItems.*; <c>Subscriptions</c> = subscriptions.* (channel follows). No transport flags —
    /// the Data API has no playback control (those ride the embedded player).
    /// </summary>
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.AcceptsSongRequests
        | MusicProviderCapabilities.Library
        | MusicProviderCapabilities.Playlists
        | MusicProviderCapabilities.Subscriptions;

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

    // ─── IMusicProviderManageApi (§3.10 — per-user manage over the youtube.manage OAuth token) ─

    public async Task<Result<IReadOnlyList<MusicPlaylistDto>>> ListPlaylistsAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<MusicPlaylistDto>>();

        string url =
            $"{YouTubeApiBase}/playlists?part=snippet,status,contentDetails&mine=true&maxResults=50";
        (HttpStatusCode? status, YouTubePlaylistListResponse? page) =
            await GetManageJsonAsync<YouTubePlaylistListResponse>(url, token, cancellationToken);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<MusicPlaylistDto>>();
        if (page?.Items is null)
            return Unavailable<IReadOnlyList<MusicPlaylistDto>>();

        IReadOnlyList<MusicPlaylistDto> playlists = page
            .Items.Select(MapPlaylistDto)
            .ToList()
            .AsReadOnly();
        return Result.Success(playlists);
    }

    public async Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        CreateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<MusicPlaylistDto>();

        object body = new
        {
            snippet = new
            {
                title = request.Name,
                description = request.Description ?? string.Empty,
            },
            status = new { privacyStatus = request.IsPublic ? "public" : "private" },
        };
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Post,
            $"{YouTubeApiBase}/playlists?part=snippet,status",
            token,
            body,
            cancellationToken
        );

        Result outcome = ManageOutcome(response, "The playlist");
        if (outcome.IsFailure)
            return outcome.WithValue<MusicPlaylistDto>(default!);

        YouTubePlaylist? created = await response!.Content.ReadFromJsonAsync<YouTubePlaylist>(
            cancellationToken: cancellationToken
        );
        if (created is null)
            return Unavailable<MusicPlaylistDto>();

        return Result.Success(MapPlaylistDto(created));
    }

    public async Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        UpdateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.Name is null && request.Description is null && request.IsPublic is null)
            return Result.Failure<MusicPlaylistDto>("Nothing to update.", "VALIDATION_FAILED");

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<MusicPlaylistDto>();

        // playlists.update (PUT) REPLACES the snippet — title is required — so read the current
        // playlist, merge the provided fields, then PUT the whole thing back carrying its id.
        string readUrl =
            $"{YouTubeApiBase}/playlists?part=snippet,status&id={Uri.EscapeDataString(playlistId)}";
        (HttpStatusCode? readStatus, YouTubePlaylistListResponse? read) =
            await GetManageJsonAsync<YouTubePlaylistListResponse>(
                readUrl,
                token,
                cancellationToken
            );
        if (readStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<MusicPlaylistDto>();

        YouTubePlaylist? existing = read?.Items?.FirstOrDefault();
        if (existing is null)
            return Result.Failure<MusicPlaylistDto>(
                "The playlist was not found on YouTube.",
                "NOT_FOUND"
            );

        string title = request.Name ?? existing.Snippet?.Title ?? string.Empty;
        string description = request.Description ?? existing.Snippet?.Description ?? string.Empty;
        string privacyStatus = request.IsPublic is bool isPublic
            ? (isPublic ? "public" : "private")
            : existing.Status?.PrivacyStatus ?? "private";

        object body = new
        {
            id = playlistId,
            snippet = new { title, description },
            status = new { privacyStatus },
        };
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Put,
            $"{YouTubeApiBase}/playlists?part=snippet,status",
            token,
            body,
            cancellationToken
        );

        Result outcome = ManageOutcome(response, "The playlist");
        if (outcome.IsFailure)
            return outcome.WithValue<MusicPlaylistDto>(default!);

        YouTubePlaylist? updated = await response!.Content.ReadFromJsonAsync<YouTubePlaylist>(
            cancellationToken: cancellationToken
        );
        if (updated is null)
            return Unavailable<MusicPlaylistDto>();

        return Result.Success(MapPlaylistDto(updated));
    }

    public async Task<Result> DeletePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        string url = $"{YouTubeApiBase}/playlists?id={Uri.EscapeDataString(playlistId)}";
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Delete,
            url,
            token,
            null,
            cancellationToken
        );
        return ManageOutcome(response, "The playlist");
    }

    public async Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        if (trackUris.Count == 0)
            return Result.Failure("No items given.", "VALIDATION_FAILED");

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        foreach (string uri in trackUris)
        {
            string? videoId = ExtractVideoId(uri);
            if (videoId is null)
                return Result.Failure("Invalid YouTube video id.", "VALIDATION_FAILED");

            object body = new
            {
                snippet = new { playlistId, resourceId = new { kind = "youtube#video", videoId } },
            };
            HttpResponseMessage? response = await SendManageAsync(
                HttpMethod.Post,
                $"{YouTubeApiBase}/playlistItems?part=snippet",
                token,
                body,
                cancellationToken
            );

            Result outcome = ManageOutcome(response, "The playlist");
            if (outcome.IsFailure)
                return outcome;
        }

        return Result.Success();
    }

    public async Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        if (trackUris.Count == 0)
            return Result.Failure("No items given.", "VALIDATION_FAILED");

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        foreach (string uri in trackUris)
        {
            string? videoId = ExtractVideoId(uri);
            if (videoId is null)
                return Result.Failure("Invalid YouTube video id.", "VALIDATION_FAILED");

            // playlistItems.delete needs the playlistItem id (NOT the video id) — resolve it first.
            string listUrl =
                $"{YouTubeApiBase}/playlistItems?part=id&playlistId={Uri.EscapeDataString(playlistId)}"
                + $"&videoId={Uri.EscapeDataString(videoId)}&maxResults=50";
            (HttpStatusCode? listStatus, YouTubePlaylistItemListResponse? list) =
                await GetManageJsonAsync<YouTubePlaylistItemListResponse>(
                    listUrl,
                    token,
                    cancellationToken
                );
            if (listStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return MissingScope();

            string? itemId = list?.Items?.FirstOrDefault()?.Id;
            if (itemId is null)
                return Result.Failure("The track was not found on the playlist.", "NOT_FOUND");

            HttpResponseMessage? response = await SendManageAsync(
                HttpMethod.Delete,
                $"{YouTubeApiBase}/playlistItems?id={Uri.EscapeDataString(itemId)}",
                token,
                null,
                cancellationToken
            );

            Result outcome = ManageOutcome(response, "The playlist");
            if (outcome.IsFailure)
                return outcome;
        }

        return Result.Success();
    }

    public async Task<Result> SaveTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) => await RateEachAsync(broadcasterId, trackUris, "like", cancellationToken);

    public async Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) => await RateEachAsync(broadcasterId, trackUris, "none", cancellationToken);

    public async Task<Result> RateTrackAsync(
        Guid broadcasterId,
        string provider,
        string trackUri,
        MusicRating rating,
        CancellationToken cancellationToken = default
    )
    {
        string? videoId = ExtractVideoId(trackUri);
        if (videoId is null)
            return Result.Failure("Invalid YouTube video id.", "VALIDATION_FAILED");

        string ratingValue = rating switch
        {
            MusicRating.Like => "like",
            MusicRating.Dislike => "dislike",
            _ => "none",
        };

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        return await RateVideoAsync(token, videoId, ratingValue, cancellationToken);
    }

    public async Task<Result> FollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    )
    {
        // YouTube's only follow analogue is a channel subscription; artist/playlist targets (gated on
        // Library at the front, which YouTube declares) fail closed here.
        if (target != MusicFollowTarget.Channel)
            return Result.Failure(
                "YouTube follows are channel subscriptions only.",
                "CAPABILITY_UNSUPPORTED"
            );

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        object body = new
        {
            snippet = new { resourceId = new { kind = "youtube#channel", channelId = targetId } },
        };
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Post,
            $"{YouTubeApiBase}/subscriptions?part=snippet",
            token,
            body,
            cancellationToken
        );
        return ManageOutcome(response, "The channel");
    }

    public async Task<Result> UnfollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    )
    {
        if (target != MusicFollowTarget.Channel)
            return Result.Failure(
                "YouTube follows are channel subscriptions only.",
                "CAPABILITY_UNSUPPORTED"
            );

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        // subscriptions.delete needs the subscription id — find the caller's subscription to the channel.
        string listUrl =
            $"{YouTubeApiBase}/subscriptions?part=id&mine=true&forChannelId={Uri.EscapeDataString(targetId)}&maxResults=1";
        (HttpStatusCode? listStatus, YouTubeSubscriptionListResponse? list) =
            await GetManageJsonAsync<YouTubeSubscriptionListResponse>(
                listUrl,
                token,
                cancellationToken
            );
        if (listStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope();

        string? subscriptionId = list?.Items?.FirstOrDefault()?.Id;
        if (subscriptionId is null)
            return Result.Failure("No subscription to that channel was found.", "NOT_FOUND");

        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Delete,
            $"{YouTubeApiBase}/subscriptions?id={Uri.EscapeDataString(subscriptionId)}",
            token,
            null,
            cancellationToken
        );
        return ManageOutcome(response, "The channel");
    }

    // ─── IMusicProviderManageApi reads (§3.10 — added 2026-07-05) ────────────

    public async Task<Result<IReadOnlyList<TrackInfo>>> GetSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<TrackInfo>>();

        // The liked-videos list is videos.list?myRating=like (OAuth, NOT the app key). YouTube paginates
        // by pageToken, not offset — offset is a no-op for this first-page read.
        int cappedLimit = Math.Clamp(limit, 1, MaxSearchResults);
        string url =
            $"{YouTubeApiBase}/videos?part=snippet,contentDetails,status&myRating=like&maxResults={cappedLimit}";
        (HttpStatusCode? status, YouTubeVideoListResponse? response) =
            await GetManageJsonAsync<YouTubeVideoListResponse>(url, token, cancellationToken);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<TrackInfo>>();
        if (response?.Items is null)
            return Unavailable<IReadOnlyList<TrackInfo>>();

        IReadOnlyList<TrackInfo> tracks = response
            .Items.Select(MapToTrackInfo)
            .ToList()
            .AsReadOnly();
        return Result.Success(tracks);
    }

    public async Task<Result<IReadOnlyList<bool>>> AreTracksSavedAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        if (trackUris.Count == 0)
            return Result.Success<IReadOnlyList<bool>>([]);

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<bool>>();

        // Map each requested uri to its video id (null → never saved), batch getRating in ≤50-id calls,
        // then read back positionally: saved ≙ rating "like".
        List<string?> videoIds = trackUris.Select(ExtractVideoId).ToList();
        List<string> distinctIds = videoIds
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Dictionary<string, string> ratingById = new(StringComparer.Ordinal);
        for (int offset = 0; offset < distinctIds.Count; offset += RatingIdsPerRequest)
        {
            List<string> chunk = distinctIds.Skip(offset).Take(RatingIdsPerRequest).ToList();
            string url =
                $"{YouTubeApiBase}/videos/getRating?id={Uri.EscapeDataString(string.Join(",", chunk))}";
            (HttpStatusCode? status, YouTubeRatingListResponse? response) =
                await GetManageJsonAsync<YouTubeRatingListResponse>(url, token, cancellationToken);

            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return MissingScope<IReadOnlyList<bool>>();
            if (response?.Items is null)
                return Unavailable<IReadOnlyList<bool>>();

            foreach (YouTubeRating item in response.Items)
            {
                if (item.VideoId is not null && item.Rating is not null)
                    ratingById[item.VideoId] = item.Rating;
            }
        }

        IReadOnlyList<bool> saved = videoIds
            .Select(id =>
                id is not null
                && ratingById.TryGetValue(id, out string? rating)
                && string.Equals(rating, "like", StringComparison.OrdinalIgnoreCase)
            )
            .ToList()
            .AsReadOnly();
        return Result.Success(saved);
    }

    public async Task<Result<IReadOnlyList<MusicFollowDto>>> GetFollowedAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        // Artist/playlist follow lists have no YouTube analogue (channels only); they gate on Library
        // at the front (which YouTube declares) and fail closed here.
        if (target != MusicFollowTarget.Channel)
            return Result.Failure<IReadOnlyList<MusicFollowDto>>(
                "YouTube follows are channel subscriptions only.",
                "CAPABILITY_UNSUPPORTED"
            );

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<MusicFollowDto>>();

        int cappedLimit = Math.Clamp(limit, 1, MaxSearchResults);
        string url =
            $"{YouTubeApiBase}/subscriptions?part=snippet&mine=true&maxResults={cappedLimit}";
        (HttpStatusCode? status, YouTubeSubscriptionListResponse? response) =
            await GetManageJsonAsync<YouTubeSubscriptionListResponse>(
                url,
                token,
                cancellationToken
            );

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<MusicFollowDto>>();
        if (response?.Items is null)
            return Unavailable<IReadOnlyList<MusicFollowDto>>();

        IReadOnlyList<MusicFollowDto> channels = response
            .Items.Where(item => item.Snippet?.ResourceId?.ChannelId is not null)
            .Select(item => new MusicFollowDto(
                item.Snippet!.ResourceId!.ChannelId!,
                item.Snippet.Title ?? string.Empty,
                BestThumbnailUrl(item.Snippet.Thumbnails)
            ))
            .ToList()
            .AsReadOnly();
        return Result.Success(channels);
    }

    // ─── Manage token (youtube.manage OAuth from the vault) ──────────────────

    /// <summary>The shared custody path (<see cref="IYouTubeAccessTokenProvider"/>) — vault lookup +
    /// transparent refresh live there, shared with the live-chat poller.</summary>
    private Task<string?> GetManageTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) => _accessTokens.GetAccessTokenAsync(broadcasterId, cancellationToken);

    // ─── Manage HTTP + failure mapping ───────────────────────────────────────

    private async Task<Result> RateVideoAsync(
        string token,
        string videoId,
        string rating,
        CancellationToken cancellationToken
    )
    {
        string url =
            $"{YouTubeApiBase}/videos/rate?id={Uri.EscapeDataString(videoId)}&rating={rating}";
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Post,
            url,
            token,
            null,
            cancellationToken
        );
        return ManageOutcome(response, "The video");
    }

    private async Task<Result> RateEachAsync(
        Guid broadcasterId,
        IReadOnlyList<string> trackUris,
        string rating,
        CancellationToken cancellationToken
    )
    {
        if (trackUris.Count == 0)
            return Result.Failure("No items given.", "VALIDATION_FAILED");

        string? token = await GetManageTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        foreach (string uri in trackUris)
        {
            string? videoId = ExtractVideoId(uri);
            if (videoId is null)
                return Result.Failure("Invalid YouTube video id.", "VALIDATION_FAILED");

            Result outcome = await RateVideoAsync(token, videoId, rating, cancellationToken);
            if (outcome.IsFailure)
                return outcome;
        }

        return Result.Success();
    }

    private async Task<HttpResponseMessage?> SendManageAsync(
        HttpMethod method,
        string url,
        string token,
        object? body,
        CancellationToken cancellationToken
    )
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "YouTube manage request failed: {Method} {Url}", method, url);
            return null;
        }
    }

    private async Task<(HttpStatusCode? Status, T? Body)> GetManageJsonAsync<T>(
        string url,
        string token,
        CancellationToken cancellationToken
    )
        where T : class
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (response.StatusCode, null);

            T? body = await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken
            );
            return (response.StatusCode, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "YouTube manage read failed: GET {Url}", url);
            return (null, null);
        }
    }

    private static Result<T> NotConnected<T>() =>
        Result.Failure<T>("YouTube is not connected for this channel.", "MISSING_SCOPE");

    private static Result NotConnected() =>
        Result.Failure("YouTube is not connected for this channel.", "MISSING_SCOPE");

    private static Result<T> MissingScope<T>() =>
        Result.Failure<T>("The YouTube connection is missing the required scope.", "MISSING_SCOPE");

    private static Result MissingScope() =>
        Result.Failure("The YouTube connection is missing the required scope.", "MISSING_SCOPE");

    private static Result<T> Unavailable<T>() =>
        Result.Failure<T>("YouTube is temporarily unavailable.", "SERVICE_UNAVAILABLE");

    private static Result ManageOutcome(HttpResponseMessage? response, string notFoundSubject)
    {
        if (response is null)
            return Result.Failure("YouTube is temporarily unavailable.", "SERVICE_UNAVAILABLE");

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure(
                "The YouTube connection is missing the required scope.",
                "MISSING_SCOPE"
            );

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure($"{notFoundSubject} was not found on YouTube.", "NOT_FOUND");

        if (!response.IsSuccessStatusCode)
            return Result.Failure("YouTube is temporarily unavailable.", "SERVICE_UNAVAILABLE");

        return Result.Success();
    }

    private static MusicPlaylistDto MapPlaylistDto(YouTubePlaylist playlist) =>
        new(
            playlist.Id ?? string.Empty,
            playlist.Snippet?.Title ?? string.Empty,
            string.IsNullOrEmpty(playlist.Snippet?.Description)
                ? null
                : playlist.Snippet!.Description,
            string.Equals(
                playlist.Status?.PrivacyStatus,
                "public",
                StringComparison.OrdinalIgnoreCase
            ),
            playlist.ContentDetails?.ItemCount ?? 0,
            BestThumbnailUrl(playlist.Snippet?.Thumbnails),
            ProviderName
        );

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

    // ─── Manage response models ───────────────────────────────────────────────

    private sealed class YouTubePlaylistListResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubePlaylist>? Items { get; set; }
    }

    private sealed class YouTubePlaylist
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public YouTubePlaylistSnippet? Snippet { get; set; }

        [JsonPropertyName("status")]
        public YouTubePlaylistStatus? Status { get; set; }

        [JsonPropertyName("contentDetails")]
        public YouTubePlaylistContentDetails? ContentDetails { get; set; }
    }

    private sealed class YouTubePlaylistSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("thumbnails")]
        public YouTubeThumbnails? Thumbnails { get; set; }
    }

    private sealed class YouTubePlaylistStatus
    {
        [JsonPropertyName("privacyStatus")]
        public string? PrivacyStatus { get; set; }
    }

    private sealed class YouTubePlaylistContentDetails
    {
        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }
    }

    private sealed class YouTubePlaylistItemListResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubePlaylistItem>? Items { get; set; }
    }

    private sealed class YouTubePlaylistItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class YouTubeSubscriptionListResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeSubscription>? Items { get; set; }
    }

    private sealed class YouTubeSubscription
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public YouTubeSubscriptionSnippet? Snippet { get; set; }
    }

    private sealed class YouTubeSubscriptionSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnails")]
        public YouTubeThumbnails? Thumbnails { get; set; }

        [JsonPropertyName("resourceId")]
        public YouTubeResourceId? ResourceId { get; set; }
    }

    private sealed class YouTubeResourceId
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }
    }

    private sealed class YouTubeRatingListResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeRating>? Items { get; set; }
    }

    private sealed class YouTubeRating
    {
        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }
    }
}
