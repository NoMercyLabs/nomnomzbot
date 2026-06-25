// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/community")]
[Authorize]
[Tags("Community")]
public class CommunityController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchModeratorsApi _moderators;
    private readonly ITwitchSubscriptionsApi _subscriptions;
    private readonly ITwitchModerationApi _moderation;
    private readonly TimeProvider _timeProvider;

    public CommunityController(
        IApplicationDbContext db,
        ITwitchChannelsApi channels,
        ITwitchModeratorsApi moderators,
        ITwitchSubscriptionsApi subscriptions,
        ITwitchModerationApi moderation,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _channels = channels;
        _moderators = moderators;
        _subscriptions = subscriptions;
        _moderation = moderation;
        _timeProvider = timeProvider;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record CommunityUserDto(
        string Id,
        string Username,
        string DisplayName,
        string? ProfileImageUrl,
        int MessageCount,
        double WatchHours,
        int CommandsUsed,
        string TrustLevel,
        bool IsBanned,
        DateTime FirstSeen,
        DateTime LastSeen
    );

    public record UserDetailDto(
        string Id,
        string Username,
        string DisplayName,
        string? ProfileImageUrl,
        int MessageCount,
        double WatchHours,
        int CommandsUsed,
        string TrustLevel,
        bool IsBanned,
        DateTime FirstSeen,
        DateTime LastSeen,
        List<ActivityDto> RecentActivity,
        List<BanRecordDto> BanHistory
    );

    public record ActivityDto(string Type, string Content, DateTime Timestamp);

    public record BanRecordDto(
        string Id,
        string BannedBy,
        string? Reason,
        DateTime BannedAt,
        DateTime? UnbannedAt
    );

    public record CommunityStatsDto(int Followers, int Subscribers, int Vips, int Moderators);

    public record BannedUserDto(
        string Id,
        string Username,
        string DisplayName,
        string? ProfileImageUrl,
        string Reason,
        string BannedBy,
        DateTime BannedAt
    );

    public record SetTrustLevelRequest(string Level);

    public record BanRequest(string Reason);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private record BanEntry(
        string UserId,
        string Username,
        string DisplayName,
        string? ProfileImageUrl,
        string Reason,
        string BannedBy,
        DateTime BannedAt
    );

    // ── Paginated user list ──────────────────────────────────────────────────

    [RequireAction("community:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<CommunityUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMembers(
        string channelId,
        [FromQuery] PageRequestDto request,
        [FromQuery] string? role,
        [FromQuery] string? cursor,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        // Followers tab: cursor-based pagination directly from Twitch (the sub-client resolves the tenant
        // Guid → Twitch id internally; a missing token / scope degrades to an empty page).
        if (string.Equals(role, "follower", StringComparison.OrdinalIgnoreCase))
        {
            Result<TwitchPage<TwitchChannelFollower>> followerPage =
                await _channels.GetChannelFollowersAsync(
                    broadcasterId,
                    new TwitchPageRequest(After: cursor, PageSize: request.Take),
                    ct
                );
            IReadOnlyList<TwitchChannelFollower> followers = followerPage.IsSuccess
                ? followerPage.Value.Items
                : [];
            string? nextCursor = followerPage.IsSuccess ? followerPage.Value.NextCursor : null;
            int total = followerPage.IsSuccess ? followerPage.Value.Total : 0;

            // Follower ids are Twitch user string ids — join on User.TwitchUserId.
            List<string> followerIds = followers.Select(f => f.UserId).ToList();

            Dictionary<string, User> users = await _db
                .Users.Where(u => followerIds.Contains(u.TwitchUserId))
                .ToDictionaryAsync(u => u.TwitchUserId, ct);

            var chatStats = await _db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == broadcasterId && followerIds.Contains(m.UserId)
                )
                .GroupBy(m => m.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    MessageCount = g.Count(),
                    FirstSeen = g.Min(m => m.CreatedAt),
                    LastSeen = g.Max(m => m.CreatedAt),
                })
                .ToDictionaryAsync(c => c.UserId, ct);

            List<CommunityUserDto> followerItems = followers
                .Select(f =>
                {
                    users.TryGetValue(f.UserId, out User? user);
                    chatStats.TryGetValue(f.UserId, out var stats);

                    return new CommunityUserDto(
                        f.UserId,
                        user?.Username ?? f.UserLogin,
                        user?.DisplayName ?? f.UserName,
                        user?.ProfileImageUrl,
                        stats?.MessageCount ?? 0,
                        0,
                        0,
                        "viewer",
                        false,
                        f.FollowedAt.UtcDateTime,
                        stats?.LastSeen ?? f.FollowedAt.UtcDateTime
                    );
                })
                .ToList();

            return Ok(
                new PaginatedResponse<CommunityUserDto>
                {
                    Data = followerItems,
                    HasMore = nextCursor is not null,
                    NextCursor = nextCursor,
                    Total = total,
                }
            );
        }

        int skip = (request.Page - 1) * request.Take;

        // VIP tab: fetch from Twitch API, paginate in-memory
        if (string.Equals(role, "vip", StringComparison.OrdinalIgnoreCase))
        {
            Result<TwitchPage<TwitchVip>> vipPage = await _moderators.GetVipsAsync(
                broadcasterId,
                new TwitchPageRequest(),
                ct
            );
            IReadOnlyList<TwitchVip> vips = vipPage.IsSuccess ? vipPage.Value.Items : [];
            int vipTotal = vips.Count;

            List<TwitchVip> pagedVips = vips.Skip(skip).Take(request.Take + 1).ToList();

            // VIP ids are Twitch user string ids — join on User.TwitchUserId.
            List<string> vipIds = pagedVips.Select(v => v.UserId).ToList();

            Dictionary<string, User> vipUsers = await _db
                .Users.Where(u => vipIds.Contains(u.TwitchUserId))
                .ToDictionaryAsync(u => u.TwitchUserId, ct);

            var vipChatStats = await _db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == broadcasterId && vipIds.Contains(m.UserId)
                )
                .GroupBy(m => m.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    MessageCount = g.Count(),
                    FirstSeen = g.Min(m => m.CreatedAt),
                    LastSeen = g.Max(m => m.CreatedAt),
                })
                .ToDictionaryAsync(c => c.UserId, ct);

            bool vipHasMore = pagedVips.Count > request.Take;

            List<CommunityUserDto> vipItems = pagedVips
                .Take(request.Take)
                .Select(v =>
                {
                    vipUsers.TryGetValue(v.UserId, out User? user);
                    vipChatStats.TryGetValue(v.UserId, out var stats);
                    return new CommunityUserDto(
                        v.UserId,
                        user?.Username ?? v.UserLogin,
                        user?.DisplayName ?? v.UserName,
                        user?.ProfileImageUrl,
                        stats?.MessageCount ?? 0,
                        0,
                        0,
                        "vip",
                        false,
                        stats?.FirstSeen
                            ?? user?.CreatedAt
                            ?? _timeProvider.GetUtcNow().UtcDateTime,
                        stats?.LastSeen ?? user?.CreatedAt ?? _timeProvider.GetUtcNow().UtcDateTime
                    );
                })
                .ToList();

            return Ok(
                new PaginatedResponse<CommunityUserDto>
                {
                    Data = vipItems,
                    NextPage = vipHasMore ? request.Page + 1 : null,
                    HasMore = vipHasMore,
                    Total = vipTotal,
                }
            );
        }

        // Candidate ids live in Twitch-user-id space (ChatMessage.UserId is the Twitch id; moderator
        // ids are projected from the moderator's User.TwitchUserId).
        List<string> candidateUserIds;

        if (string.Equals(role, "moderator", StringComparison.OrdinalIgnoreCase))
        {
            candidateUserIds = await _db
                .ChannelModerators.Where(cm => cm.ChannelId == broadcasterId)
                .Select(cm => cm.User.TwitchUserId)
                .ToListAsync(ct);
        }
        else
        {
            // No role filter (all users): chatters + mods
            List<string> chattedIds = await _db
                .ChatMessages.Where(m => m.BroadcasterId == broadcasterId)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);

            List<string> modIds = await _db
                .ChannelModerators.Where(cm => cm.ChannelId == broadcasterId)
                .Select(cm => cm.User.TwitchUserId)
                .ToListAsync(ct);

            candidateUserIds = chattedIds.Union(modIds).Distinct().ToList();
        }

        int totalCount = candidateUserIds.Count;

        // Chat stats for message counts / first/last seen
        var chatStats2 = await _db
            .ChatMessages.Where(m =>
                m.BroadcasterId == broadcasterId && candidateUserIds.Contains(m.UserId)
            )
            .GroupBy(m => m.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                MessageCount = g.Count(),
                FirstSeen = g.Min(m => m.CreatedAt),
                LastSeen = g.Max(m => m.CreatedAt),
            })
            .ToDictionaryAsync(c => c.UserId, ct);

        // Paginate the candidate list
        List<string> pagedIds = candidateUserIds
            .OrderBy(id => id)
            .Skip(skip)
            .Take(request.Take + 1)
            .ToList();

        Dictionary<string, User> users2 = await _db
            .Users.Where(u => pagedIds.Contains(u.TwitchUserId))
            .ToDictionaryAsync(u => u.TwitchUserId, ct);

        HashSet<string> moderatorIds = await _db
            .ChannelModerators.Where(cm =>
                cm.ChannelId == broadcasterId && pagedIds.Contains(cm.User.TwitchUserId)
            )
            .Select(cm => cm.User.TwitchUserId)
            .ToHashSetAsync(ct);

        Dictionary<string, string> trustConfigs = await _db
            .Configurations.Where(c =>
                c.BroadcasterId == broadcasterId && c.Key.StartsWith("trust:")
            )
            .ToDictionaryAsync(c => c.Key, c => c.Value ?? "viewer", ct);

        HashSet<string> bannedIds = await _db
            .Configurations.Where(c => c.BroadcasterId == broadcasterId && c.Key.StartsWith("ban:"))
            .Select(c => c.Key.Substring(4))
            .ToHashSetAsync(ct);

        List<CommunityUserDto> items = pagedIds
            .Take(request.Take)
            .Select(userId =>
            {
                users2.TryGetValue(userId, out User? user);
                chatStats2.TryGetValue(userId, out var stats);

                string trustLevel =
                    trustConfigs.TryGetValue($"trust:{userId}", out string? t) ? t
                    : moderatorIds.Contains(userId) ? "moderator"
                    : "viewer";

                bool isBanned = bannedIds.Contains(userId);

                return new CommunityUserDto(
                    userId,
                    user?.Username ?? "",
                    user?.DisplayName ?? "",
                    user?.ProfileImageUrl,
                    stats?.MessageCount ?? 0,
                    0,
                    0,
                    trustLevel,
                    isBanned,
                    stats?.FirstSeen ?? user?.CreatedAt ?? _timeProvider.GetUtcNow().UtcDateTime,
                    stats?.LastSeen ?? user?.CreatedAt ?? _timeProvider.GetUtcNow().UtcDateTime
                );
            })
            .ToList();

        bool hasMore = pagedIds.Count > request.Take;

        return Ok(
            new PaginatedResponse<CommunityUserDto>
            {
                Data = items,
                NextPage = hasMore ? request.Page + 1 : null,
                HasMore = hasMore,
                Total = totalCount,
            }
        );
    }

    // ── Community stats ──────────────────────────────────────────────────────

    [RequireAction("community:read")]
    [HttpGet("stats")]
    [ProducesResponseType<StatusResponseDto<CommunityStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        // Followers, subscribers, and VIP count from Twitch (authoritative). Each sub-client resolves the
        // tenant Guid internally and returns a Result; a failure (no token / missing scope) leaves the value
        // at 0 — surfaced for the user through the scope-diagnostics endpoint rather than thrown here.
        Result<int> followerResult = await _channels.GetChannelFollowerCountAsync(
            broadcasterId,
            ct
        );
        int followers = followerResult.IsSuccess ? followerResult.Value : 0;

        Result<int> subscriberResult = await _subscriptions.GetSubscriberCountAsync(
            broadcasterId,
            ct
        );
        int subscribers = subscriberResult.IsSuccess ? subscriberResult.Value : 0;

        Result<TwitchPage<TwitchVip>> vipResult = await _moderators.GetVipsAsync(
            broadcasterId,
            new TwitchPageRequest(),
            ct
        );
        int vipCount = vipResult.IsSuccess ? vipResult.Value.Items.Count : 0;

        // Moderators: use the DB as the primary source because the community list
        // already reads from this table and it reflects onboarding sync.
        int moderatorCount = await _db.ChannelModerators.CountAsync(
            cm => cm.ChannelId == broadcasterId,
            ct
        );

        return Ok(
            new StatusResponseDto<CommunityStatsDto>
            {
                Data = new CommunityStatsDto(followers, subscribers, vipCount, moderatorCount),
            }
        );
    }

    // ── Banned users list ────────────────────────────────────────────────────

    [RequireAction("community:read")]
    [HttpGet("bans")]
    [ProducesResponseType<PaginatedResponse<BannedUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBans(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        int skip = (request.Page - 1) * request.Take;

        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        List<ConfigEntity> banConfigs = await _db
            .Configurations.Where(c => c.BroadcasterId == broadcasterId && c.Key.StartsWith("ban:"))
            .OrderByDescending(c => c.CreatedAt)
            .Skip(skip)
            .Take(request.Take + 1)
            .ToListAsync(ct);

        List<BannedUserDto> items = banConfigs
            .Take(request.Take)
            .Select(c =>
            {
                BanEntry? entry = c.Value is not null
                    ? JsonSerializer.Deserialize<BanEntry>(c.Value, JsonOptions)
                    : null;

                return new BannedUserDto(
                    entry?.UserId ?? c.Key.Substring(4),
                    entry?.Username ?? "",
                    entry?.DisplayName ?? "",
                    entry?.ProfileImageUrl,
                    entry?.Reason ?? "",
                    entry?.BannedBy ?? "",
                    entry?.BannedAt ?? c.CreatedAt
                );
            })
            .ToList();

        bool hasMore = banConfigs.Count > request.Take;

        return Ok(
            new PaginatedResponse<BannedUserDto>
            {
                Data = items,
                NextPage = hasMore ? request.Page + 1 : null,
                HasMore = hasMore,
            }
        );
    }

    // ── User detail ──────────────────────────────────────────────────────────

    [RequireAction("community:read")]
    [HttpGet("{userId}")]
    [ProducesResponseType<StatusResponseDto<UserDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserDetail(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        // userId is the Twitch user string id (as exposed by the list DTOs).
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.TwitchUserId == userId, ct);
        if (user is null)
            return NotFoundResponse("User not found.");

        var messageStats = await _db
            .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && m.UserId == userId)
            .GroupBy(m => 1)
            .Select(g => new
            {
                Count = g.Count(),
                FirstSeen = g.Min(m => m.CreatedAt),
                LastSeen = g.Max(m => m.CreatedAt),
            })
            .FirstOrDefaultAsync(ct);

        List<ActivityDto> recentMessages = await _db
            .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .Select(m => new ActivityDto(
                m.IsCommand ? "command" : "message",
                m.Message,
                m.CreatedAt
            ))
            .ToListAsync(ct);

        bool isModerator = await _db.ChannelModerators.AnyAsync(
            cm => cm.ChannelId == broadcasterId && cm.User.TwitchUserId == userId,
            ct
        );

        ConfigEntity? trustConfig = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == $"trust:{userId}",
            ct
        );

        string trustLevel = trustConfig?.Value ?? (isModerator ? "moderator" : "viewer");

        ConfigEntity? banConfig = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == $"ban:{userId}",
            ct
        );

        bool isBanned = banConfig is not null;

        List<BanRecordDto> banHistory = [];
        if (banConfig?.Value is not null)
        {
            BanEntry? entry = JsonSerializer.Deserialize<BanEntry>(banConfig.Value, JsonOptions);
            if (entry is not null)
            {
                banHistory.Add(
                    new BanRecordDto(
                        $"ban:{userId}",
                        entry.BannedBy,
                        entry.Reason,
                        entry.BannedAt,
                        null
                    )
                );
            }
        }

        UserDetailDto detail = new UserDetailDto(
            user.TwitchUserId,
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            messageStats?.Count ?? 0,
            0,
            0,
            trustLevel,
            isBanned,
            messageStats?.FirstSeen ?? user.CreatedAt,
            messageStats?.LastSeen ?? user.CreatedAt,
            recentMessages,
            banHistory
        );

        return Ok(new StatusResponseDto<UserDetailDto> { Data = detail });
    }

    // ── Set trust level ───────────────────────────────────────────────────────

    [RequireAction("community:trust:write")]
    [HttpPut("{userId}/trust")]
    [ProducesResponseType<StatusResponseDto<UserDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetTrustLevel(
        string channelId,
        string userId,
        [FromBody] SetTrustLevelRequest request,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == $"trust:{userId}",
            ct
        );

        if (config is null)
        {
            _db.Configurations.Add(
                new ConfigEntity
                {
                    BroadcasterId = broadcasterId,
                    Key = $"trust:{userId}",
                    Value = request.Level,
                }
            );
        }
        else
        {
            config.Value = request.Level;
        }

        await _db.SaveChangesAsync(ct);

        return await GetUserDetail(channelId, userId, ct);
    }

    // ── Ban user ──────────────────────────────────────────────────────────────

    [RequireAction("moderation:ban")]
    [HttpPost("{userId}/ban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> BanUser(
        string channelId,
        string userId,
        [FromBody] BanRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        string moderatorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // userId is the Twitch user string id (as exposed by the list DTOs). The sub-client resolves the
        // tenant Guid internally; the local ban record below is written regardless (best-effort enforcement).
        await _moderation.BanUserAsync(broadcasterId, userId, request.Reason, ct);

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.TwitchUserId == userId, ct);

        BanEntry entry = new BanEntry(
            userId,
            user?.Username ?? "",
            user?.DisplayName ?? "",
            user?.ProfileImageUrl,
            request.Reason,
            moderatorId,
            _timeProvider.GetUtcNow().UtcDateTime
        );

        ConfigEntity? existing = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == $"ban:{userId}",
            ct
        );

        string json = JsonSerializer.Serialize(entry, JsonOptions);

        if (existing is null)
        {
            _db.Configurations.Add(
                new ConfigEntity
                {
                    BroadcasterId = broadcasterId,
                    Key = $"ban:{userId}",
                    Value = json,
                }
            );
        }
        else
        {
            existing.Value = json;
        }

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Unban user ────────────────────────────────────────────────────────────

    [RequireAction("moderation:unban")]
    [HttpDelete("{userId}/ban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnbanUser(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        await _moderation.UnbanUserAsync(broadcasterId, userId, ct);

        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == $"ban:{userId}",
            ct
        );

        if (config is not null)
        {
            _db.Configurations.Remove(config);
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // ── Add VIP ───────────────────────────────────────────────────────────────

    [RequireAction("moderation:vip")]
    [HttpPost("{userId}/vip")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddVip(string channelId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _moderators.AddVipAsync(broadcasterId, userId, ct);
        return result.IsFailure ? ResultResponse(result) : NoContent();
    }

    // ── Remove VIP ────────────────────────────────────────────────────────────

    [RequireAction("moderation:vip")]
    [HttpDelete("{userId}/vip")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveVip(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _moderators.RemoveVipAsync(broadcasterId, userId, ct);
        return result.IsFailure ? ResultResponse(result) : NoContent();
    }
}
