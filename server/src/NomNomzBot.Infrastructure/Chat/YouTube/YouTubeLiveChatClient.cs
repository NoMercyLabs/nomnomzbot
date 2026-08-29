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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// <see cref="IYouTubeLiveChatClient"/> over the YouTube Live Streaming API (Data API v3). Reads ride the
/// broadcaster's own <c>youtube.readonly</c> OAuth bearer (the app key cannot read a live chat), mirroring the
/// manage-plane pattern in <c>YouTubeMusicProvider</c>. Every transport/HTTP failure degrades to a typed
/// <see cref="Result"/> failure (never throws). A 401, or a 403 whose Google error body carries no quota-shaped
/// <c>reason</c>, maps to <c>MISSING_SCOPE</c> so the poller can trigger re-auth; a 403 whose body carries
/// <c>quotaExceeded</c>/<c>rateLimitExceeded</c>/<c>dailyLimitExceeded</c> maps instead to the distinct
/// <c>QUOTA_EXCEEDED</c> code so quota burn is never mistaken for — and doesn't trigger — a scope re-grant.
/// </summary>
public sealed class YouTubeLiveChatClient : IYouTubeLiveChatClient
{
    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";

    private readonly HttpClient _http;
    private readonly ILogger<YouTubeLiveChatClient> _logger;

    public YouTubeLiveChatClient(
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeLiveChatClient> logger
    )
    {
        _http = httpClientFactory.CreateClient("youtube");
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<YouTubeActiveChat>>> GetActiveLiveChatsAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    )
    {
        // broadcastStatus/mine/id are mutually exclusive; broadcastStatus=active on the caller's token
        // returns EVERY one of their live broadcasts (a channel can run more than one concurrently — e.g.
        // simultaneous multi-encoder streams), so every item whose snippet carries liveChatId is tracked,
        // never just the first. liveStreamingDetails supplies the concurrent-viewer sample.
        string url =
            $"{YouTubeApiBase}/liveBroadcasts?part=snippet,liveStreamingDetails&broadcastStatus=active";

        (HttpStatusCode? status, LiveBroadcastListResponse? body, string? errorCode) =
            await GetAsync<LiveBroadcastListResponse>(url, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<IReadOnlyList<YouTubeActiveChat>>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (errorCode is not null)
            return Result.Failure<IReadOnlyList<YouTubeActiveChat>>(
                DescribeErrorCode(errorCode),
                errorCode
            );

        // Not live (no active broadcast, or every active broadcast has chat disabled) — a normal state,
        // not a failure. The poller treats an empty list as "nothing to read right now".
        List<YouTubeActiveChat> active =
        [
            .. (body?.Items ?? [])
                .Where(b => !string.IsNullOrEmpty(b.Snippet?.LiveChatId))
                .Select(b => new YouTubeActiveChat(
                    b.Id ?? string.Empty,
                    b.Snippet!.LiveChatId!,
                    b.Snippet.Title,
                    ParseConcurrentViewers(b.LiveStreamingDetails?.ConcurrentViewers)
                )),
        ];

        return Result.Success<IReadOnlyList<YouTubeActiveChat>>(active);
    }

    /// <summary><c>liveStreamingDetails.concurrentViewers</c> comes back as a decimal STRING on the wire
    /// (Google's uint64-as-string convention) — an unparsable or absent value degrades to null rather than
    /// failing the whole read.</summary>
    private static long? ParseConcurrentViewers(string? raw) =>
        !string.IsNullOrEmpty(raw) && long.TryParse(raw, out long value) ? value : null;

    public async Task<Result<YouTubeLiveChatPage>> ListMessagesAsync(
        string accessToken,
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken = default
    )
    {
        string url =
            $"{YouTubeApiBase}/liveChatMessages?part=snippet,authorDetails"
            + $"&liveChatId={Uri.EscapeDataString(liveChatId)}";
        if (!string.IsNullOrEmpty(pageToken))
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

        (HttpStatusCode? status, LiveChatMessageListResponse? body, string? errorCode) =
            await GetAsync<LiveChatMessageListResponse>(url, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<YouTubeLiveChatPage>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (errorCode is not null)
            return Result.Failure<YouTubeLiveChatPage>(DescribeErrorCode(errorCode), errorCode);
        // The chat ended or the id is stale — surface it so the poller re-resolves the active broadcast.
        if (status is HttpStatusCode.NotFound)
            return Result.Failure<YouTubeLiveChatPage>(
                "The YouTube live chat is no longer available.",
                "NOT_FOUND"
            );
        if (body is null)
            return Result.Failure<YouTubeLiveChatPage>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );

        List<YouTubeLiveChatMessage> messages =
        [
            .. (body.Items ?? [])
                .Where(item => item.Snippet is not null && item.AuthorDetails is not null)
                .Select(MapMessage),
        ];

        return Result.Success(
            new YouTubeLiveChatPage(messages, body.NextPageToken, body.PollingIntervalMillis)
        );
    }

    public async Task<Result<YouTubeOwnChannel>> GetOwnChannelAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{YouTubeApiBase}/channels?part=snippet&mine=true&maxResults=1";

        (HttpStatusCode? status, ChannelListResponse? body, string? errorCode) =
            await GetAsync<ChannelListResponse>(url, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<YouTubeOwnChannel>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (errorCode is not null)
            return Result.Failure<YouTubeOwnChannel>(DescribeErrorCode(errorCode), errorCode);

        ChannelItem? channel = body?.Items?.FirstOrDefault(c => !string.IsNullOrEmpty(c.Id));
        if (channel is null)
            return Result.Failure<YouTubeOwnChannel>(
                "The Google account has no YouTube channel.",
                "NOT_FOUND"
            );

        return Result.Success(
            new YouTubeOwnChannel(channel.Id!, channel.Snippet?.Title ?? string.Empty)
        );
    }

    public async Task<Result> SendMessageAsync(
        string accessToken,
        string liveChatId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        // The Live Chat API rejects >200-char messages — fail closed locally with the precise reason
        // instead of burning a quota-billed call on a guaranteed 400.
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure("The message is empty.", "VALIDATION_FAILED");
        if (text.Length > 200)
            return Result.Failure(
                "YouTube live chat messages are capped at 200 characters.",
                "VALIDATION_FAILED"
            );

        string url = $"{YouTubeApiBase}/liveChatMessages?part=snippet";
        HttpRequestMessage request = new(HttpMethod.Post, url);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(
            new
            {
                snippet = new
                {
                    liveChatId,
                    type = "textMessageEvent",
                    textMessageDetails = new { messageText = text },
                },
            }
        );

        return await SendWriteAsync(request, $"send to chat {liveChatId}", cancellationToken);
    }

    public async Task<Result<string>> BanUserAsync(
        string accessToken,
        string liveChatId,
        string bannedChannelId,
        int? durationSeconds,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{YouTubeApiBase}/liveChat/bans?part=snippet";
        HttpRequestMessage request = new(HttpMethod.Post, url);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(
            durationSeconds is { } seconds
                ? new
                {
                    snippet = new
                    {
                        liveChatId,
                        type = "temporary",
                        banDurationSeconds = seconds,
                        bannedUserDetails = new { channelId = bannedChannelId },
                    },
                }
                : (object)
                    new
                    {
                        snippet = new
                        {
                            liveChatId,
                            type = "permanent",
                            bannedUserDetails = new { channelId = bannedChannelId },
                        },
                    }
        );

        // The insert response's resource id is the ONLY key liveChatBans.delete accepts — surface it so
        // the platform can ledger it for a later unban.
        Result<LiveChatBanResource> created = await SendWriteForBodyAsync<LiveChatBanResource>(
            request,
            $"ban in chat {liveChatId}",
            cancellationToken
        );
        if (created.IsFailure)
            return Result.Failure<string>(
                created.ErrorMessage!,
                created.ErrorCode,
                created.ErrorDetail
            );
        if (string.IsNullOrEmpty(created.Value.Id))
            return Result.Failure<string>(
                "YouTube returned a ban without a resource id.",
                "SERVICE_UNAVAILABLE"
            );

        return Result.Success(created.Value.Id!);
    }

    public async Task<Result> UnbanUserAsync(
        string accessToken,
        string banId,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{YouTubeApiBase}/liveChat/bans?id={Uri.EscapeDataString(banId)}";
        HttpRequestMessage request = new(HttpMethod.Delete, url);
        request.Headers.Authorization = new("Bearer", accessToken);

        return await SendWriteAsync(request, $"unban {banId}", cancellationToken);
    }

    public async Task<Result> DeleteMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{YouTubeApiBase}/liveChat/messages?id={Uri.EscapeDataString(messageId)}";
        HttpRequestMessage request = new(HttpMethod.Delete, url);
        request.Headers.Authorization = new("Bearer", accessToken);

        return await SendWriteAsync(request, $"delete message {messageId}", cancellationToken);
    }

    public async Task<Result<string>> UpdateActiveBroadcastTitleAsync(
        string accessToken,
        string title,
        CancellationToken cancellationToken = default
    )
    {
        // YouTube caps a broadcast title at 100 characters — fail closed locally, same rationale as the
        // 200-char chat-message cap (never burn a quota-billed call on a guaranteed 400).
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<string>("The title is empty.", "VALIDATION_FAILED");
        if (title.Length > 100)
            return Result.Failure<string>(
                "YouTube broadcast titles are capped at 100 characters.",
                "VALIDATION_FAILED"
            );

        // The update REPLACES the snippet, so the current broadcasts are fetched first: each id keys its
        // own PUT and its scheduledStartTime must be carried over (required by the API on a snippet
        // update). No maxResults=1 here — a channel can run more than one concurrent broadcast (e.g.
        // simultaneous multi-encoder streams), and every one of them needs the new title, not just the
        // first (mirrors the multi-broadcast enumeration in GetActiveLiveChatsAsync above).
        string getUrl = $"{YouTubeApiBase}/liveBroadcasts?part=snippet&broadcastStatus=active";
        (HttpStatusCode? status, LiveBroadcastListResponse? body, string? errorCode) =
            await GetAsync<LiveBroadcastListResponse>(getUrl, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<string>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (errorCode is not null)
            return Result.Failure<string>(DescribeErrorCode(errorCode), errorCode);

        List<LiveBroadcastItem> broadcasts =
        [
            .. (body?.Items ?? []).Where(b => !string.IsNullOrEmpty(b.Id)),
        ];
        if (broadcasts.Count == 0)
            return Result.Failure<string>(
                "The channel has no active YouTube broadcast to retitle.",
                "NOT_FOUND"
            );

        // Every active broadcast is retitled; a partial failure is surfaced truthfully rather than
        // reported as a blanket success — the caller must know not everything got renamed.
        int failureCount = 0;
        foreach (LiveBroadcastItem broadcast in broadcasts)
        {
            string putUrl = $"{YouTubeApiBase}/liveBroadcasts?part=snippet";
            HttpRequestMessage request = new(HttpMethod.Put, putUrl);
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Content = JsonContent.Create(
                new
                {
                    id = broadcast.Id,
                    snippet = new
                    {
                        title,
                        scheduledStartTime = broadcast.Snippet?.ScheduledStartTime,
                    },
                }
            );

            Result updated = await SendWriteAsync(
                request,
                $"retitle broadcast {broadcast.Id}",
                cancellationToken
            );
            if (updated.IsFailure)
                failureCount++;
        }

        return failureCount == 0
            ? Result.Success(title)
            : Result.Failure<string>(
                failureCount == broadcasts.Count
                    ? "The title update failed on every active YouTube broadcast."
                    : $"The title update failed on {failureCount} of {broadcasts.Count} active YouTube broadcasts.",
                "PARTIAL_FAILURE"
            );
    }

    /// <summary>Shared write-call outcome mapping: 401 → MISSING_SCOPE; 403 → MISSING_SCOPE, or the
    /// distinct QUOTA_EXCEEDED when Google's error body names a quota/rate reason; 404 → NOT_FOUND; other
    /// non-success → SERVICE_UNAVAILABLE; transport exceptions degrade the same way (never throw).</summary>
    private async Task<Result> SendWriteAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            string? errorCode = await ClassifyErrorAsync(response, cancellationToken);
            if (errorCode is not null)
                return Result.Failure(DescribeErrorCode(errorCode), errorCode);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure("The YouTube live chat is no longer available.", "NOT_FOUND");
            if (!response.IsSuccessStatusCode)
                return Result.Failure("YouTube is temporarily unavailable.", "SERVICE_UNAVAILABLE");

            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "YouTube live-chat write threw for {Operation}", operation);
            return Result.Failure("YouTube is temporarily unavailable.", "SERVICE_UNAVAILABLE");
        }
    }

    /// <summary>The <see cref="SendWriteAsync"/> variant for writes whose RESPONSE BODY the caller needs
    /// (the ban insert returns the deletable resource) — same outcome mapping, plus body deserialization
    /// (an unreadable body on a 2xx degrades to SERVICE_UNAVAILABLE, never throws).</summary>
    private async Task<Result<TBody>> SendWriteForBodyAsync<TBody>(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken
    )
        where TBody : class
    {
        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            string? errorCode = await ClassifyErrorAsync(response, cancellationToken);
            if (errorCode is not null)
                return Result.Failure<TBody>(DescribeErrorCode(errorCode), errorCode);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<TBody>(
                    "The YouTube live chat is no longer available.",
                    "NOT_FOUND"
                );
            if (!response.IsSuccessStatusCode)
                return Result.Failure<TBody>(
                    "YouTube is temporarily unavailable.",
                    "SERVICE_UNAVAILABLE"
                );

            TBody? body = await response.Content.ReadFromJsonAsync<TBody>(
                cancellationToken: cancellationToken
            );
            return body is null
                ? Result.Failure<TBody>(
                    "YouTube is temporarily unavailable.",
                    "SERVICE_UNAVAILABLE"
                )
                : Result.Success(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "YouTube live-chat write threw for {Operation}", operation);
            return Result.Failure<TBody>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        }
    }

    /// <summary>Quota/rate reason codes Google's error body reports for chat writes — distinguished from a
    /// genuine scope/permission 403 so an exhausted quota never fires the 15-minute scope re-auth path.</summary>
    private static readonly HashSet<string> QuotaReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "quotaExceeded",
        "rateLimitExceeded",
        "dailyLimitExceeded",
        "userRateLimitExceeded",
    };

    /// <summary>Classifies a non-success response into the closed <c>MISSING_SCOPE</c>/<c>QUOTA_EXCEEDED</c>
    /// pair, or <c>null</c> when the status is neither 401 nor 403 (the caller maps 404/other itself). A 401
    /// is always MISSING_SCOPE (an invalid/expired bearer, never a quota concern). A 403's Google error body
    /// is parsed for <c>error.errors[].reason</c>: a quota/rate reason maps to QUOTA_EXCEEDED, any other
    /// reason (or none — an unparsable/empty body) defaults to MISSING_SCOPE, the safe assumption for a
    /// permission-shaped 403.</summary>
    private static async Task<string?> ClassifyErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return "MISSING_SCOPE";
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return null;

        string? reason = await TryReadFirstReasonAsync(response, cancellationToken);
        return reason is not null && QuotaReasons.Contains(reason)
            ? "QUOTA_EXCEEDED"
            : "MISSING_SCOPE";
    }

    private static async Task<string?> TryReadFirstReasonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            GoogleErrorEnvelope? envelope =
                await response.Content.ReadFromJsonAsync<GoogleErrorEnvelope>(
                    cancellationToken: cancellationToken
                );
            return envelope?.Error?.Errors?.FirstOrDefault()?.Reason;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unparsable/empty error body is not itself a failure — it just means no reason is
            // available, so the caller falls back to the safe MISSING_SCOPE default.
            return null;
        }
    }

    private static string DescribeErrorCode(string errorCode) =>
        errorCode == "QUOTA_EXCEEDED"
            ? "The YouTube API quota has been exhausted for this connection."
            : "The YouTube connection is missing the required scope.";

    private static YouTubeLiveChatMessage MapMessage(LiveChatMessageItem item) =>
        new(
            item.Id ?? string.Empty,
            item.AuthorDetails!.ChannelId ?? string.Empty,
            item.AuthorDetails.DisplayName ?? string.Empty,
            item.Snippet!.DisplayMessage ?? string.Empty,
            item.Snippet.PublishedAt ?? DateTimeOffset.MinValue,
            item.AuthorDetails.IsChatModerator,
            item.AuthorDetails.IsChatOwner,
            item.AuthorDetails.IsChatSponsor,
            item.Snippet.Type ?? "textMessageEvent",
            MapSuperChat(item.Snippet.SuperChatDetails),
            MapSuperSticker(item.Snippet.SuperStickerDetails),
            MapNewSponsor(item.Snippet.NewSponsorDetails),
            MapMemberMilestone(item.Snippet.MemberMilestoneChatDetails),
            MapMembershipGifting(item.Snippet.MembershipGiftingDetails),
            MapGiftMembershipReceived(item.Snippet.GiftMembershipReceivedDetails)
        );

    private static YouTubeSuperChatDetails? MapSuperChat(LiveChatSuperChatDetails? d) =>
        d is null
            ? null
            : new(
                d.AmountMicros,
                d.Currency ?? string.Empty,
                d.AmountDisplayString ?? string.Empty,
                d.UserComment ?? string.Empty,
                d.Tier
            );

    private static YouTubeSuperStickerDetails? MapSuperSticker(LiveChatSuperStickerDetails? d) =>
        d is null
            ? null
            : new(
                d.SuperStickerMetadata?.StickerId ?? string.Empty,
                d.SuperStickerMetadata?.AltText ?? string.Empty,
                d.AmountMicros,
                d.Currency ?? string.Empty,
                d.AmountDisplayString ?? string.Empty,
                d.Tier
            );

    private static YouTubeNewSponsorDetails? MapNewSponsor(LiveChatNewSponsorDetails? d) =>
        d is null ? null : new(d.MemberLevelName ?? string.Empty, d.IsUpgrade);

    private static YouTubeMemberMilestoneChatDetails? MapMemberMilestone(
        LiveChatMemberMilestoneChatDetails? d
    ) =>
        d is null
            ? null
            : new(d.UserComment ?? string.Empty, d.MemberMonth, d.MemberLevelName ?? string.Empty);

    private static YouTubeMembershipGiftingDetails? MapMembershipGifting(
        LiveChatMembershipGiftingDetails? d
    ) => d is null ? null : new(d.GiftMembershipsCount, d.GiftMembershipsLevelName ?? string.Empty);

    private static YouTubeGiftMembershipReceivedDetails? MapGiftMembershipReceived(
        LiveChatGiftMembershipReceivedDetails? d
    ) =>
        d is null
            ? null
            : new(
                d.MemberLevelName ?? string.Empty,
                d.GifterChannelId ?? string.Empty,
                d.AssociatedMembershipGiftingMessageId ?? string.Empty
            );

    /// <summary>Reads and deserializes a GET; on a non-success status the returned <c>ErrorCode</c> is
    /// <c>MISSING_SCOPE</c>/<c>QUOTA_EXCEEDED</c> for a 401/403 (see <see cref="ClassifyErrorAsync"/>) or
    /// <c>null</c> for any other status, leaving 404/other mapping to the caller.</summary>
    private async Task<(HttpStatusCode? Status, T? Body, string? ErrorCode)> GetAsync<T>(
        string url,
        string accessToken,
        CancellationToken cancellationToken
    )
        where T : class
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "YouTube live-chat read failed: {Status} for {Path}",
                    response.StatusCode,
                    new Uri(url).AbsolutePath
                );
                string? errorCode = await ClassifyErrorAsync(response, cancellationToken);
                return (response.StatusCode, null, errorCode);
            }

            T? body = await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken
            );
            return (response.StatusCode, body, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "YouTube live-chat read threw for {Path}",
                new Uri(url).AbsolutePath
            );
            return (null, null, null);
        }
    }

    // ─── Wire models (YouTube Data API v3 live) ──────────────────────────────

    private sealed class LiveBroadcastListResponse
    {
        [JsonPropertyName("items")]
        public List<LiveBroadcastItem>? Items { get; set; }
    }

    private sealed class LiveBroadcastItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveBroadcastSnippet? Snippet { get; set; }

        [JsonPropertyName("liveStreamingDetails")]
        public LiveStreamingDetails? LiveStreamingDetails { get; set; }
    }

    // Google reports concurrentViewers as a decimal string (uint64-as-string convention) — parsed by
    // ParseConcurrentViewers, degrading to null when absent/unparsable rather than failing the read.
    private sealed class LiveStreamingDetails
    {
        [JsonPropertyName("concurrentViewers")]
        public string? ConcurrentViewers { get; set; }
    }

    private sealed class LiveBroadcastSnippet
    {
        [JsonPropertyName("liveChatId")]
        public string? LiveChatId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        // Carried over on a snippet update (the PUT replaces the snippet and the API requires it).
        [JsonPropertyName("scheduledStartTime")]
        public string? ScheduledStartTime { get; set; }
    }

    private sealed class LiveChatMessageListResponse
    {
        [JsonPropertyName("pollingIntervalMillis")]
        public int PollingIntervalMillis { get; set; }

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("items")]
        public List<LiveChatMessageItem>? Items { get; set; }
    }

    private sealed class LiveChatMessageItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LiveChatMessageSnippet? Snippet { get; set; }

        [JsonPropertyName("authorDetails")]
        public LiveChatAuthorDetails? AuthorDetails { get; set; }
    }

    private sealed class LiveChatMessageSnippet
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("displayMessage")]
        public string? DisplayMessage { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("superChatDetails")]
        public LiveChatSuperChatDetails? SuperChatDetails { get; set; }

        [JsonPropertyName("superStickerDetails")]
        public LiveChatSuperStickerDetails? SuperStickerDetails { get; set; }

        [JsonPropertyName("newSponsorDetails")]
        public LiveChatNewSponsorDetails? NewSponsorDetails { get; set; }

        [JsonPropertyName("memberMilestoneChatDetails")]
        public LiveChatMemberMilestoneChatDetails? MemberMilestoneChatDetails { get; set; }

        [JsonPropertyName("membershipGiftingDetails")]
        public LiveChatMembershipGiftingDetails? MembershipGiftingDetails { get; set; }

        [JsonPropertyName("giftMembershipReceivedDetails")]
        public LiveChatGiftMembershipReceivedDetails? GiftMembershipReceivedDetails { get; set; }
    }

    // snippet.type == "superChatEvent"
    private sealed class LiveChatSuperChatDetails
    {
        [JsonPropertyName("amountMicros")]
        public ulong AmountMicros { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amountDisplayString")]
        public string? AmountDisplayString { get; set; }

        [JsonPropertyName("userComment")]
        public string? UserComment { get; set; }

        [JsonPropertyName("tier")]
        public uint Tier { get; set; }
    }

    // snippet.type == "superStickerEvent"
    private sealed class LiveChatSuperStickerDetails
    {
        [JsonPropertyName("superStickerMetadata")]
        public LiveChatSuperStickerMetadata? SuperStickerMetadata { get; set; }

        [JsonPropertyName("amountMicros")]
        public ulong AmountMicros { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amountDisplayString")]
        public string? AmountDisplayString { get; set; }

        [JsonPropertyName("tier")]
        public uint Tier { get; set; }
    }

    private sealed class LiveChatSuperStickerMetadata
    {
        [JsonPropertyName("stickerId")]
        public string? StickerId { get; set; }

        [JsonPropertyName("altText")]
        public string? AltText { get; set; }
    }

    // snippet.type == "newSponsorEvent"
    private sealed class LiveChatNewSponsorDetails
    {
        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }

        [JsonPropertyName("isUpgrade")]
        public bool IsUpgrade { get; set; }
    }

    // snippet.type == "memberMilestoneChatEvent"
    private sealed class LiveChatMemberMilestoneChatDetails
    {
        [JsonPropertyName("userComment")]
        public string? UserComment { get; set; }

        [JsonPropertyName("memberMonth")]
        public uint MemberMonth { get; set; }

        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }
    }

    // snippet.type == "membershipGiftingEvent"
    private sealed class LiveChatMembershipGiftingDetails
    {
        [JsonPropertyName("giftMembershipsCount")]
        public int GiftMembershipsCount { get; set; }

        [JsonPropertyName("giftMembershipsLevelName")]
        public string? GiftMembershipsLevelName { get; set; }
    }

    // snippet.type == "giftMembershipReceivedEvent"
    private sealed class LiveChatGiftMembershipReceivedDetails
    {
        [JsonPropertyName("memberLevelName")]
        public string? MemberLevelName { get; set; }

        [JsonPropertyName("gifterChannelId")]
        public string? GifterChannelId { get; set; }

        [JsonPropertyName("associatedMembershipGiftingMessageId")]
        public string? AssociatedMembershipGiftingMessageId { get; set; }
    }

    private sealed class LiveChatAuthorDetails
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isChatModerator")]
        public bool IsChatModerator { get; set; }

        [JsonPropertyName("isChatOwner")]
        public bool IsChatOwner { get; set; }

        [JsonPropertyName("isChatSponsor")]
        public bool IsChatSponsor { get; set; }
    }

    // The liveChatBans.insert response — only the resource id matters (the liveChatBans.delete key).
    private sealed class LiveChatBanResource
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class ChannelListResponse
    {
        [JsonPropertyName("items")]
        public List<ChannelItem>? Items { get; set; }
    }

    private sealed class ChannelItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public ChannelSnippet? Snippet { get; set; }
    }

    private sealed class ChannelSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    // Google's standard API error envelope — only the reason code needed to distinguish a quota/rate 403
    // from a genuine scope/permission 403 (see ClassifyErrorAsync).
    private sealed class GoogleErrorEnvelope
    {
        [JsonPropertyName("error")]
        public GoogleErrorBody? Error { get; set; }
    }

    private sealed class GoogleErrorBody
    {
        [JsonPropertyName("errors")]
        public List<GoogleErrorItem>? Errors { get; set; }
    }

    private sealed class GoogleErrorItem
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
