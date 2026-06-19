// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Transport;

public interface ITwitchApiService
{
    Task<TwitchUserInfo?> GetUserInfoAsync(string userId, CancellationToken ct = default);
    Task<TwitchStreamInfo?> GetStreamInfoAsync(
        string broadcasterId,
        CancellationToken ct = default
    );
    Task<bool> TimeoutUserAsync(
        string broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken ct = default
    );
    Task<bool> BanUserAsync(
        string broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken ct = default
    );
    Task<bool> UnbanUserAsync(string broadcasterId, string userId, CancellationToken ct = default);
    Task<bool> ShoutoutAsync(
        string broadcasterId,
        string toUserId,
        string moderatorId,
        CancellationToken ct = default
    );

    /// <summary>Send a chat message via Helix POST /chat/messages (EventSub-first path).</summary>
    Task<bool> SendChatMessageAsync(
        string broadcasterId,
        string senderUserId,
        string message,
        string? replyParentMessageId = null,
        CancellationToken ct = default
    );

    /// <summary>Delete a specific chat message via Helix.</summary>
    Task<bool> DeleteChatMessageAsync(
        string broadcasterId,
        string messageId,
        CancellationToken ct = default
    );

    /// <summary>Get all custom channel point rewards manageable by the bot.</summary>
    Task<IReadOnlyList<TwitchRewardInfo>> GetCustomRewardsAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update the status of a channel point redemption (FULFILLED or CANCELED).</summary>
    Task<bool> UpdateRedemptionStatusAsync(
        string broadcasterId,
        string rewardId,
        string redemptionId,
        string status,
        CancellationToken ct = default
    );

    /// <summary>Update stream title, game, and/or tags via PATCH /helix/channels.</summary>
    Task<bool> UpdateChannelInfoAsync(
        string broadcasterId,
        string? title,
        string? gameId,
        List<string>? tags,
        CancellationToken ct = default
    );

    /// <summary>Search Twitch categories/games by name.</summary>
    Task<IReadOnlyList<TwitchCategoryInfo>> SearchCategoriesAsync(
        string query,
        CancellationToken ct = default
    );

    /// <summary>Add a user as moderator in a channel.</summary>
    Task<bool> AddModeratorAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    );

    /// <summary>Get the total follower count for a channel.</summary>
    Task<int> GetFollowerCountAsync(string broadcasterId, CancellationToken ct = default);

    /// <summary>Get the total subscriber count for a channel.</summary>
    Task<int> GetSubscriberCountAsync(string broadcasterId, CancellationToken ct = default);

    /// <summary>Get banned users for a channel.</summary>
    Task<IReadOnlyList<TwitchBannedUser>> GetBannedUsersAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get moderators for a channel.</summary>
    Task<IReadOnlyList<TwitchModeratorInfo>> GetModeratorsAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get VIPs for a channel.</summary>
    Task<IReadOnlyList<TwitchVipInfo>> GetVipsAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get channel info (title, game, tags, language) via GET /helix/channels.</summary>
    Task<TwitchChannelInfo?> GetChannelInfoAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get a page of followers for a channel. Returns items, next cursor, and total count.</summary>
    Task<(
        IReadOnlyList<TwitchFollowerInfo> Items,
        string? NextCursor,
        int Total
    )> GetFollowersAsync(
        string broadcasterId,
        string? after = null,
        int pageSize = 100,
        CancellationToken ct = default
    );

    /// <summary>Get channels that a user moderates via GET /helix/moderation/channels.</summary>
    Task<IReadOnlyList<TwitchModeratedChannel>> GetModeratedChannelsAsync(
        string userId,
        CancellationToken ct = default
    );
}

public sealed record TwitchRewardInfo(
    string Id,
    string Title,
    int Cost,
    bool IsEnabled,
    string? Prompt,
    bool UserInputRequired
);

public record TwitchUserInfo(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    string BroadcasterType
);

public record TwitchStreamInfo(
    string Id,
    string UserId,
    string? GameId,
    string? GameName,
    string? Title,
    bool IsLive,
    int ViewerCount,
    DateTime? StartedAt = null
);

public record TwitchCategoryInfo(string Id, string Name, string? BoxArtUrl);

public record TwitchBannedUser(
    string UserId,
    string UserLogin,
    string UserName,
    string Reason,
    DateTime? ExpiresAt
);

public record TwitchModeratorInfo(string UserId, string UserLogin, string UserName);

public record TwitchVipInfo(string UserId, string UserLogin, string UserName);

public record TwitchChannelInfo(
    string BroadcasterId,
    string Title,
    string GameName,
    string GameId,
    List<string> Tags,
    string Language
);

public record TwitchFollowerInfo(
    string UserId,
    string UserLogin,
    string UserName,
    DateTime FollowedAt
);

public record TwitchModeratedChannel(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName
);
