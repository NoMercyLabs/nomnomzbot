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
/// <see cref="Result"/> failure (never throws), and a missing/expired scope maps to <c>MISSING_SCOPE</c> so the
/// poller can trigger re-auth rather than crash a background loop.
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

    public async Task<Result<YouTubeActiveChat?>> GetActiveLiveChatAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    )
    {
        // broadcastStatus/mine/id are mutually exclusive; broadcastStatus=active on the caller's token returns
        // only their live broadcasts, so one item (if any) is the active one whose snippet carries liveChatId.
        string url =
            $"{YouTubeApiBase}/liveBroadcasts?part=snippet&broadcastStatus=active&maxResults=1";

        (HttpStatusCode? status, LiveBroadcastListResponse? body) =
            await GetAsync<LiveBroadcastListResponse>(url, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<YouTubeActiveChat?>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure<YouTubeActiveChat?>(
                "The YouTube connection is missing the required scope.",
                "MISSING_SCOPE"
            );

        LiveBroadcastItem? broadcast = body?.Items?.FirstOrDefault(b =>
            !string.IsNullOrEmpty(b.Snippet?.LiveChatId)
        );

        // Not live (no active broadcast, or an active broadcast with chat disabled) — a normal state, not a
        // failure. The poller treats a null value as "nothing to read right now".
        if (broadcast?.Snippet?.LiveChatId is not { } liveChatId)
            return Result.Success<YouTubeActiveChat?>(null);

        return Result.Success<YouTubeActiveChat?>(
            new YouTubeActiveChat(broadcast.Id ?? string.Empty, liveChatId, broadcast.Snippet.Title)
        );
    }

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

        (HttpStatusCode? status, LiveChatMessageListResponse? body) =
            await GetAsync<LiveChatMessageListResponse>(url, accessToken, cancellationToken);

        if (status is null)
            return Result.Failure<YouTubeLiveChatPage>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure<YouTubeLiveChatPage>(
                "The YouTube connection is missing the required scope.",
                "MISSING_SCOPE"
            );
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

        (HttpStatusCode? status, ChannelListResponse? body) = await GetAsync<ChannelListResponse>(
            url,
            accessToken,
            cancellationToken
        );

        if (status is null)
            return Result.Failure<YouTubeOwnChannel>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure<YouTubeOwnChannel>(
                "The YouTube connection is missing the required scope.",
                "MISSING_SCOPE"
            );

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

    public async Task<Result> BanUserAsync(
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
            durationSeconds is int seconds
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

        return await SendWriteAsync(request, $"ban in chat {liveChatId}", cancellationToken);
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

    /// <summary>Shared write-call outcome mapping: 401/403 → MISSING_SCOPE, 404 → NOT_FOUND, other
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
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result.Failure(
                    "The YouTube connection is missing the required scope.",
                    "MISSING_SCOPE"
                );
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

    private static YouTubeLiveChatMessage MapMessage(LiveChatMessageItem item) =>
        new(
            item.Id ?? string.Empty,
            item.AuthorDetails!.ChannelId ?? string.Empty,
            item.AuthorDetails.DisplayName ?? string.Empty,
            item.Snippet!.DisplayMessage ?? string.Empty,
            item.Snippet.PublishedAt ?? DateTimeOffset.MinValue,
            item.AuthorDetails.IsChatModerator,
            item.AuthorDetails.IsChatOwner,
            item.AuthorDetails.IsChatSponsor
        );

    private async Task<(HttpStatusCode? Status, T? Body)> GetAsync<T>(
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
                return (response.StatusCode, null);
            }

            T? body = await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken
            );
            return (response.StatusCode, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "YouTube live-chat read threw for {Path}",
                new Uri(url).AbsolutePath
            );
            return (null, null);
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
    }

    private sealed class LiveBroadcastSnippet
    {
        [JsonPropertyName("liveChatId")]
        public string? LiveChatId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
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
        [JsonPropertyName("displayMessage")]
        public string? DisplayMessage { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; set; }
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
}
