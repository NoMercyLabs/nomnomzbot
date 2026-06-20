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
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Platform.Transport;

/// <summary>
/// Twitch Helix API client.
/// Tokens are loaded from the Service entity (Name="twitch_bot" for the shared bot account,
/// Name="twitch" for per-broadcaster tokens needed for mod actions).
/// On 401, attempts a single token refresh and retries.
/// Respects Ratelimit-Reset header on 429.
/// </summary>
public sealed class TwitchApiService : ITwitchApiService
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ITwitchAuthService _authService;
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly HttpClient _http;
    private readonly TwitchOptions _options;
    private readonly ILogger<TwitchApiService> _logger;
    private readonly TimeProvider _timeProvider;

    private const string HelixBase = "https://api.twitch.tv/helix";

    public TwitchApiService(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ITwitchAuthService authService,
        ITwitchIdentityResolver identityResolver,
        IHttpClientFactory httpClientFactory,
        IOptions<TwitchOptions> options,
        ILogger<TwitchApiService> logger,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _authService = authService;
        _identityResolver = identityResolver;
        _http = httpClientFactory.CreateClient("twitch-helix");
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    public async Task<TwitchUserInfo?> GetUserInfoAsync(
        string userId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo = await GetBotTokenAsync(
            ct
        );
        if (tokenInfo is null)
            return null;

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            $"{HelixBase}/users?id={Uri.EscapeDataString(userId)}",
            tokenInfo.Value,
            null,
            ct
        );

        if (response is null)
            return null;

        HelixDataResponse<HelixUser>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixUser>
        >(cancellationToken: ct);
        HelixUser? user = data?.Data?.FirstOrDefault();
        if (user is null)
            return null;

        return new(
            user.Id,
            user.Login,
            user.DisplayName,
            user.ProfileImageUrl,
            user.BroadcasterType
        );
    }

    public async Task<TwitchStreamInfo?> GetStreamInfoAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo = await GetBotTokenAsync(
            ct
        );
        if (tokenInfo is null)
            return null;

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            $"{HelixBase}/streams?user_id={Uri.EscapeDataString(broadcasterId)}",
            tokenInfo.Value,
            null,
            ct
        );

        if (response is null)
            return null;

        HelixDataResponse<HelixStream>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixStream>
        >(cancellationToken: ct);
        HelixStream? stream = data?.Data?.FirstOrDefault();

        // When the stream is offline, the array is empty
        return stream is not null
            ? new(
                stream.Id,
                stream.UserId,
                stream.GameId,
                stream.GameName,
                stream.Title,
                true,
                stream.ViewerCount
            )
            : new TwitchStreamInfo(string.Empty, broadcasterId, null, null, null, false, 0);
    }

    public async Task<bool> TimeoutUserAsync(
        string broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken ct = default
    )
    {
        return await BanOrTimeoutAsync(broadcasterId, userId, durationSeconds, reason, ct);
    }

    public async Task<bool> BanUserAsync(
        string broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken ct = default
    )
    {
        return await BanOrTimeoutAsync(broadcasterId, userId, null, reason, ct);
    }

    public async Task<bool> UnbanUserAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
        {
            _logger.LogWarning(
                "No moderator token for broadcaster {BroadcasterId}, skipping unban",
                broadcasterId
            );
            return false;
        }

        string url =
            $"{HelixBase}/moderation/bans"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&moderator_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&user_id={Uri.EscapeDataString(userId)}";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Delete,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        return response is { IsSuccessStatusCode: true };
    }

    public async Task<bool> SendChatMessageAsync(
        string broadcasterId,
        string senderUserId,
        string message,
        string? replyParentMessageId = null,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo = await GetBotTokenAsync(
            ct
        );
        if (tokenInfo is null)
        {
            _logger.LogWarning(
                "No bot token available, cannot send chat message to {BroadcasterId}",
                broadcasterId
            );
            return false;
        }

        object body = replyParentMessageId is not null
            ? (object)
                new
                {
                    broadcaster_id = broadcasterId,
                    sender_id = senderUserId,
                    message,
                    reply_parent_message_id = replyParentMessageId,
                }
            : (object)
                new
                {
                    broadcaster_id = broadcasterId,
                    sender_id = senderUserId,
                    message,
                };

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Post,
            $"{HelixBase}/chat/messages",
            tokenInfo.Value,
            body,
            ct
        );
        if (response is null)
            return false;

        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Helix SendChatMessage failed for {BroadcasterId}: {Status} — {Error}",
                broadcasterId,
                response.StatusCode,
                err
            );
            return false;
        }

        return true;
    }

    public async Task<bool> DeleteChatMessageAsync(
        string broadcasterId,
        string messageId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return false;

        string url =
            $"{HelixBase}/moderation/chat"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&moderator_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&message_id={Uri.EscapeDataString(messageId)}";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Delete,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        return response is { IsSuccessStatusCode: true };
    }

    public async Task<bool> ShoutoutAsync(
        string broadcasterId,
        string toUserId,
        string moderatorId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? token =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (token is null)
        {
            _logger.LogWarning(
                "No moderator token for broadcaster {BroadcasterId}, skipping shoutout",
                broadcasterId
            );
            return false;
        }

        string url =
            $"{HelixBase}/chat/shoutouts"
            + $"?from_broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&to_broadcaster_id={Uri.EscapeDataString(toUserId)}"
            + $"&moderator_id={Uri.EscapeDataString(moderatorId)}";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Post,
            url,
            token.Value,
            new { },
            ct
        );
        return response?.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK;
    }

    public async Task<IReadOnlyList<TwitchRewardInfo>> GetCustomRewardsAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return [];

        string url =
            $"{HelixBase}/channel_points/custom_rewards"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + "&only_manageable_rewards=true";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            string err = response is null ? "null" : await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "GetCustomRewards failed for {BroadcasterId}: {Error}",
                broadcasterId,
                err
            );
            return [];
        }

        HelixDataResponse<HelixCustomReward>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixCustomReward>
        >(cancellationToken: ct);

        return (data?.Data ?? [])
            .Select(r => new TwitchRewardInfo(
                r.Id,
                r.Title,
                r.Cost,
                r.IsEnabled,
                r.Prompt,
                r.IsUserInputRequired
            ))
            .ToList();
    }

    public async Task<bool> UpdateRedemptionStatusAsync(
        string broadcasterId,
        string rewardId,
        string redemptionId,
        string status,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return false;

        string url =
            $"{HelixBase}/channel_points/custom_rewards/redemptions"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&reward_id={Uri.EscapeDataString(rewardId)}"
            + $"&id={Uri.EscapeDataString(redemptionId)}";

        HttpResponseMessage? response = await SendHelixAsync(
            new("PATCH"),
            url,
            tokenInfo.Value,
            new { status },
            ct
        );
        return response is { IsSuccessStatusCode: true };
    }

    public async Task<bool> UpdateChannelInfoAsync(
        string broadcasterId,
        string? title,
        string? gameId,
        List<string>? tags,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
        {
            _logger.LogWarning(
                "No broadcaster token for {BroadcasterId}, cannot update channel info",
                broadcasterId
            );
            return false;
        }

        string url = $"{HelixBase}/channels?broadcaster_id={Uri.EscapeDataString(broadcasterId)}";

        var body = new Dictionary<string, object?>();
        if (title is not null)
            body["title"] = title;
        if (gameId is not null)
            body["game_id"] = gameId;
        if (tags is not null)
            body["tags"] = tags;

        HttpResponseMessage? response = await SendHelixAsync(
            new("PATCH"),
            url,
            tokenInfo.Value,
            body,
            ct
        );

        if (response is null || !response.IsSuccessStatusCode)
        {
            string err = response is null ? "null" : await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "UpdateChannelInfo failed for {BroadcasterId}: {Status} — {Error}",
                broadcasterId,
                response?.StatusCode,
                err
            );
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<TwitchCategoryInfo>> SearchCategoriesAsync(
        string query,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo = await GetBotTokenAsync(
            ct
        );
        if (tokenInfo is null)
            return [];

        string url = $"{HelixBase}/search/categories?query={Uri.EscapeDataString(query)}&first=10";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
            return [];

        HelixDataResponse<HelixCategory>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixCategory>
        >(cancellationToken: ct);

        return (data?.Data ?? [])
            .Select(c => new TwitchCategoryInfo(c.Id, c.Name, c.BoxArtUrl))
            .ToList();
    }

    public async Task<bool> AddModeratorAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return false;

        string url =
            $"{HelixBase}/moderation/moderators?broadcaster_id={broadcasterId}&user_id={userId}";
        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Post,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        return response is not null && response.IsSuccessStatusCode;
    }

    public async Task<int> GetFollowerCountAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return 0;

        string url =
            $"{HelixBase}/channels/followers?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=1";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetFollowerCount failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return 0;
        }

        HelixTotalResponse? data = await response.Content.ReadFromJsonAsync<HelixTotalResponse>(
            cancellationToken: ct
        );
        return data?.Total ?? 0;
    }

    public async Task<int> GetSubscriberCountAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return 0;

        string url =
            $"{HelixBase}/subscriptions?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=1";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetSubscriberCount failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return 0;
        }

        HelixTotalResponse? data = await response.Content.ReadFromJsonAsync<HelixTotalResponse>(
            cancellationToken: ct
        );
        return data?.Total ?? 0;
    }

    public async Task<IReadOnlyList<TwitchBannedUser>> GetBannedUsersAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return [];

        string url =
            $"{HelixBase}/moderation/banned?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=100";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetBannedUsers failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return [];
        }

        HelixDataResponse<HelixBannedUser>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixBannedUser>
        >(cancellationToken: ct);

        return (data?.Data ?? [])
            .Select(b => new TwitchBannedUser(
                b.UserId,
                b.UserLogin,
                b.UserName,
                b.Reason ?? "",
                b.ExpiresAt
            ))
            .ToList();
    }

    public async Task<IReadOnlyList<TwitchModeratorInfo>> GetModeratorsAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return [];

        string url =
            $"{HelixBase}/moderation/moderators?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=100";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetModerators failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return [];
        }

        HelixDataResponse<HelixModeratorUser>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixModeratorUser>
        >(cancellationToken: ct);

        return (data?.Data ?? [])
            .Select(m => new TwitchModeratorInfo(m.UserId, m.UserLogin, m.UserName))
            .ToList();
    }

    public async Task<IReadOnlyList<TwitchVipInfo>> GetVipsAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return [];

        string url =
            $"{HelixBase}/channels/vips?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=100";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetVips failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return [];
        }

        HelixDataResponse<HelixVipUser>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixVipUser>
        >(cancellationToken: ct);

        return (data?.Data ?? [])
            .Select(v => new TwitchVipInfo(v.UserId, v.UserLogin, v.UserName))
            .ToList();
    }

    public async Task<TwitchChannelInfo?> GetChannelInfoAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo = await GetBotTokenAsync(
            ct
        );
        if (tokenInfo is null)
            return null;

        string url = $"{HelixBase}/channels?broadcaster_id={Uri.EscapeDataString(broadcasterId)}";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetChannelInfo failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return null;
        }

        HelixDataResponse<HelixChannelInfo>? data = await response.Content.ReadFromJsonAsync<
            HelixDataResponse<HelixChannelInfo>
        >(cancellationToken: ct);
        HelixChannelInfo? channel = data?.Data?.FirstOrDefault();
        if (channel is null)
            return null;

        return new TwitchChannelInfo(
            channel.BroadcasterId,
            channel.Title ?? "",
            channel.GameName ?? "",
            channel.GameId ?? "",
            channel.Tags ?? [],
            channel.BroadcasterLanguage ?? ""
        );
    }

    public async Task<(
        IReadOnlyList<TwitchFollowerInfo> Items,
        string? NextCursor,
        int Total
    )> GetFollowersAsync(
        string broadcasterId,
        string? after = null,
        int pageSize = 100,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
            return ([], null, 0);

        string url =
            $"{HelixBase}/channels/followers?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first={Math.Clamp(pageSize, 1, 100)}";
        if (!string.IsNullOrEmpty(after))
            url += $"&after={Uri.EscapeDataString(after)}";

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Get,
            url,
            tokenInfo.Value,
            null,
            ct
        );
        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetFollowers failed for {BroadcasterId}: {Status}",
                broadcasterId,
                response?.StatusCode
            );
            return ([], null, 0);
        }

        HelixFollowersResponse? data =
            await response.Content.ReadFromJsonAsync<HelixFollowersResponse>(cancellationToken: ct);
        if (data is null)
            return ([], null, 0);

        List<TwitchFollowerInfo> items = (data.Data ?? [])
            .Select(f => new TwitchFollowerInfo(f.UserId, f.UserLogin, f.UserName, f.FollowedAt))
            .ToList();

        return (items, data.Pagination?.Cursor, data.Total);
    }

    public async Task<IReadOnlyList<TwitchModeratedChannel>> GetModeratedChannelsAsync(
        string userId,
        CancellationToken ct = default
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(userId, ct);
        if (tokenInfo is null)
            return [];

        List<TwitchModeratedChannel> result = [];
        string? cursor = null;

        do
        {
            string url =
                $"{HelixBase}/moderation/channels?user_id={Uri.EscapeDataString(userId)}&first=100";
            if (cursor is not null)
                url += $"&after={Uri.EscapeDataString(cursor)}";

            HttpResponseMessage? response = await SendHelixAsync(
                HttpMethod.Get,
                url,
                tokenInfo.Value,
                null,
                ct
            );
            if (response is null || !response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetModeratedChannels failed for {UserId}: {Status}",
                    userId,
                    response?.StatusCode
                );
                break;
            }

            HelixDataWithCursorResponse<HelixModeratedChannel>? data =
                await response.Content.ReadFromJsonAsync<
                    HelixDataWithCursorResponse<HelixModeratedChannel>
                >(cancellationToken: ct);
            if (data?.Data is null || data.Data.Count == 0)
                break;

            result.AddRange(
                data.Data.Select(c => new TwitchModeratedChannel(
                    c.BroadcasterId,
                    c.BroadcasterLogin,
                    c.BroadcasterName
                ))
            );
            cursor = data.Pagination?.Cursor;
        } while (cursor is not null);

        return result;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    private async Task<bool> BanOrTimeoutAsync(
        string broadcasterId,
        string userId,
        int? durationSeconds,
        string? reason,
        CancellationToken ct
    )
    {
        (string Token, Guid? BroadcasterId, string ServiceName)? tokenInfo =
            await GetModeratorTokenAsync(broadcasterId, ct);
        if (tokenInfo is null)
        {
            _logger.LogWarning(
                "No moderator token for broadcaster {BroadcasterId}, skipping ban/timeout",
                broadcasterId
            );
            return false;
        }

        string url =
            $"{HelixBase}/moderation/bans"
            + $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}"
            + $"&moderator_id={Uri.EscapeDataString(broadcasterId)}";

        object body = durationSeconds.HasValue
            ? (object)
                new
                {
                    data = new
                    {
                        user_id = userId,
                        duration = durationSeconds.Value,
                        reason = reason ?? string.Empty,
                    },
                }
            : (object)new { data = new { user_id = userId, reason = reason ?? string.Empty } };

        HttpResponseMessage? response = await SendHelixAsync(
            HttpMethod.Post,
            url,
            tokenInfo.Value,
            body,
            ct
        );
        return response is { IsSuccessStatusCode: true };
    }

    /// <summary>
    /// Sends a Helix request with bearer auth. Handles 429 backoff and 401 refresh-and-retry.
    /// </summary>
    private async Task<HttpResponseMessage?> SendHelixAsync(
        HttpMethod method,
        string url,
        (string Token, Guid? BroadcasterId, string ServiceName) tokenInfo,
        object? body,
        CancellationToken ct
    )
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpRequestMessage request = new(method, url);
            request.Headers.Add("Authorization", $"Bearer {tokenInfo.Token}");
            request.Headers.Add("Client-Id", _options.ClientId);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Helix request failed: {Method} {Url}", method, url);
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (
                    response.Headers.TryGetValues(
                        "Ratelimit-Reset",
                        out IEnumerable<string>? values
                    ) && long.TryParse(values.First(), out long resetEpoch)
                )
                {
                    TimeSpan delay =
                        DateTimeOffset.FromUnixTimeSeconds(resetEpoch) - _timeProvider.GetUtcNow();
                    if (delay > TimeSpan.Zero)
                    {
                        _logger.LogWarning("Helix rate limited, waiting {Delay:g}", delay);
                        await Task.Delay(delay, ct);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
                continue;
            }

            if (
                response.StatusCode == HttpStatusCode.Unauthorized
                && attempt == 0
                && tokenInfo.BroadcasterId is not null
            )
            {
                _logger.LogDebug(
                    "Helix 401, refreshing token for {BroadcasterId}",
                    tokenInfo.BroadcasterId
                );
                TokenResult? refreshed = await _authService.RefreshTokenAsync(
                    tokenInfo.BroadcasterId,
                    tokenInfo.ServiceName,
                    ct
                );
                if (refreshed is null)
                    return response;

                tokenInfo = tokenInfo with { Token = refreshed.AccessToken };
                continue;
            }

            return response;
        }

        return null;
    }

    /// <summary>Get bot account token (Name="twitch_bot", no BroadcasterId).</summary>
    private async Task<(string Token, Guid? BroadcasterId, string ServiceName)?> GetBotTokenAsync(
        CancellationToken ct
    )
    {
        Service? service = await _db
            .Services.Where(s => s.Name == "twitch_bot" && s.Enabled && s.AccessToken != null)
            .OrderByDescending(s => s.TokenExpiry)
            .FirstOrDefaultAsync(ct);

        if (service?.AccessToken is null)
            return null;

        // SubjectId is the tenant Guid string, or the platform sentinel for the shared bot row.
        string subjectId = service.BroadcasterId?.ToString() ?? "_platform";
        string? token = await _tokenProtector.TryUnprotectAsync(
            service.AccessToken,
            new TokenProtectionContext(subjectId, service.Name, "access"),
            ct
        );
        if (token is null)
            return null;

        return (token, service.BroadcasterId, service.Name);
    }

    /// <summary>
    /// Get moderator/broadcaster token for a specific channel (Name="twitch").
    /// <paramref name="twitchBroadcasterId"/> is the Twitch STRING id; it is resolved to the tenant
    /// Guid to look up the per-tenant Service token row.
    /// </summary>
    private async Task<(
        string Token,
        Guid? BroadcasterId,
        string ServiceName
    )?> GetModeratorTokenAsync(string twitchBroadcasterId, CancellationToken ct)
    {
        Guid? tenantId = await _identityResolver.GetBroadcasterIdAsync(twitchBroadcasterId, ct);
        if (tenantId is null)
        {
            // Unknown channel — fall back to the shared bot token.
            return await GetBotTokenAsync(ct);
        }

        Service? service = await _db.Services.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == tenantId
                && s.Name == "twitch"
                && s.Enabled
                && s.AccessToken != null,
            ct
        );

        if (service?.AccessToken is null)
        {
            // Fall back to bot token
            return await GetBotTokenAsync(ct);
        }

        string? token = await _tokenProtector.TryUnprotectAsync(
            service.AccessToken,
            new TokenProtectionContext(tenantId.Value.ToString(), service.Name, "access"),
            ct
        );
        if (token is null)
            return null;

        return (token, tenantId, service.Name);
    }

    // ─── Helix response models ────────────────────────────────────────────────────

    private sealed class HelixDataResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
    }

    private sealed class HelixDataWithCursorResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }

        [JsonPropertyName("pagination")]
        public HelixPagination? Pagination { get; set; }
    }

    private sealed class HelixPagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }
    }

    private sealed class HelixFollowersResponse
    {
        [JsonPropertyName("data")]
        public List<HelixFollower>? Data { get; set; }

        [JsonPropertyName("pagination")]
        public HelixPagination? Pagination { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class HelixFollower
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = null!;

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = null!;

        [JsonPropertyName("followed_at")]
        public DateTime FollowedAt { get; set; }
    }

    private sealed class HelixModeratedChannel
    {
        [JsonPropertyName("broadcaster_id")]
        public string BroadcasterId { get; set; } = null!;

        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; set; } = null!;

        [JsonPropertyName("broadcaster_name")]
        public string BroadcasterName { get; set; } = null!;
    }

    private sealed class HelixUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("login")]
        public string Login { get; set; } = null!;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = null!;

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("broadcaster_type")]
        public string BroadcasterType { get; set; } = null!;
    }

    private sealed class HelixCustomReward
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("title")]
        public string Title { get; set; } = null!;

        [JsonPropertyName("cost")]
        public int Cost { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("is_user_input_required")]
        public bool IsUserInputRequired { get; set; }
    }

    private sealed class HelixCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("box_art_url")]
        public string? BoxArtUrl { get; set; }
    }

    private sealed class HelixStream
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("game_id")]
        public string? GameId { get; set; }

        [JsonPropertyName("game_name")]
        public string? GameName { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("viewer_count")]
        public int ViewerCount { get; set; }
    }

    /// <summary>Response shape for endpoints that return a top-level "total" field (followers, subscriptions).</summary>
    private sealed class HelixTotalResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class HelixBannedUser
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = null!;

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = null!;

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("expires_at")]
        [JsonConverter(typeof(EmptyStringDateTimeConverter))]
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>Handles Twitch returning "" instead of null for permanent bans.</summary>
    private sealed class EmptyStringDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            string? str = reader.GetString();
            if (string.IsNullOrEmpty(str))
                return null;
            return DateTime.TryParse(str, out var dt) ? dt : null;
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTime? value,
            JsonSerializerOptions options
        )
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString("o"));
            else
                writer.WriteNullValue();
        }
    }

    private sealed class HelixModeratorUser
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = null!;

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = null!;
    }

    private sealed class HelixVipUser
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = null!;

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = null!;
    }

    private sealed class HelixChannelInfo
    {
        [JsonPropertyName("broadcaster_id")]
        public string BroadcasterId { get; set; } = null!;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("game_name")]
        public string? GameName { get; set; }

        [JsonPropertyName("game_id")]
        public string? GameId { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("broadcaster_language")]
        public string? BroadcasterLanguage { get; set; }
    }
}
