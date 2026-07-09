// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

public class ChannelService : IChannelService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IEventBus _eventBus;

    public ChannelService(IApplicationDbContext db, TimeProvider timeProvider, IEventBus eventBus)
    {
        _db = db;
        _timeProvider = timeProvider;
        _eventBus = eventBus;
    }

    public async Task<Result> JoinAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        channel.Enabled = true;
        channel.BotJoinedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> LeaveAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        channel.Enabled = false;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ChannelDto>> GetAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        Channel? channel = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        return Result.Success(ToDto(channel));
    }

    public async Task<Result<IReadOnlyList<ChannelSummaryDto>>> GetAllActiveAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelSummaryDto> channels = await _db
            .Channels.Include(c => c.User)
            .Where(c => c.Enabled && c.IsOnboarded)
            .OrderBy(c => c.Name)
            .Select(c => new ChannelSummaryDto(
                c.Id.ToString(),
                c.Name,
                c.User.DisplayName,
                c.User.ProfileImageUrl,
                c.IsLive,
                "broadcaster",
                null,
                c.OverlayToken,
                c.User.Color
            ))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ChannelSummaryDto>>(channels);
    }

    public async Task<Result<PagedList<ChannelSummaryDto>>> GetChannelsAsync(
        string userId,
        PaginationParams pagination,
        IReadOnlyList<string>? additionalChannelIds = null,
        CancellationToken cancellationToken = default
    )
    {
        // userId is the internal user Guid in string form. additionalChannelIds are external Twitch
        // channel ids (from the Twitch moderation API) and resolve against Channels.TwitchChannelId.
        Guid.TryParse(userId, out Guid userGuid);

        IReadOnlyList<string> extraTwitchChannelIds = additionalChannelIds ?? [];

        // Return channels where the user is the broadcaster (owner), a DB-tracked moderator,
        // or present in the caller-supplied Twitch-id list.
        IQueryable<Channel> query = _db
            .Channels.Include(c => c.User)
            .Where(c =>
                c.OwnerUserId == userGuid
                || c.Moderators.Any(m => m.UserId == userGuid)
                || extraTwitchChannelIds.Contains(c.TwitchChannelId)
            );

        int total = await query.CountAsync(cancellationToken);

        // The caller's OWN channel(s) sort first, then alphabetical. This is load-bearing: the dashboard defaults
        // its active channel to the first item, and resolves the caller's role against it. An owner who also
        // moderates a channel whose name sorts earlier (e.g. owns "stoney_eagle" but moderates "aaoa_") would
        // otherwise default to that moderated channel and be resolved as a mere viewer there — landing on the
        // participant surface instead of their own management dashboard. Owning a channel is the strongest claim,
        // so it wins the default.
        List<ChannelSummaryDto> items = await query
            .OrderByDescending(c => c.OwnerUserId == userGuid)
            .ThenBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new ChannelSummaryDto(
                c.Id.ToString(),
                c.Name,
                c.User.DisplayName,
                c.User.ProfileImageUrl,
                c.IsLive,
                c.OwnerUserId == userGuid ? "broadcaster" : "moderator",
                null,
                c.OverlayToken,
                c.User.Color
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<ChannelSummaryDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<ChannelDto>> UpdateSettingsAsync(
        string broadcasterId,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        Channel? channel = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        if (request.DisplayName is not null)
            channel.User.DisplayName = request.DisplayName;
        if (request.Locale is not null)
            channel.Language = request.Locale;
        if (request.AutoJoin.HasValue)
            channel.Enabled = request.AutoJoin.Value;

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterGuid,
                Domain = "channel-settings",
                Action = "updated",
            },
            cancellationToken
        );
        return Result.Success(ToDto(channel));
    }

    public async Task<Result<ChannelDto>> OnboardAsync(
        string broadcasterId,
        CreateChannelRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // broadcasterId identifies the owning user (internal User Guid in string form). The channel's
        // own surrogate id is generated on creation; its Twitch id comes from the owner's TwitchUserId.
        if (!Guid.TryParse(broadcasterId, out Guid ownerGuid))
            return Result.Failure<ChannelDto>(
                "User not found. Cannot onboard channel.",
                "NOT_FOUND"
            );

        Channel? existing = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.OwnerUserId == ownerGuid, cancellationToken);

        if (existing is not null)
        {
            existing.IsOnboarded = true;
            existing.BotJoinedAt ??= _timeProvider.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
            await PublishOnboardedAsync(existing, cancellationToken);
            return Result.Success(ToDto(existing));
        }

        // Check if user exists
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerGuid, cancellationToken);
        if (user is null)
            return Result.Failure<ChannelDto>(
                "User not found. Cannot onboard channel.",
                "NOT_FOUND"
            );

        Channel channel = new()
        {
            OwnerUserId = user.Id,
            TwitchChannelId = user.TwitchUserId,
            ExternalChannelId = user.TwitchUserId!,
            Name = user.Username,
            NameNormalized = user.Username.ToLowerInvariant(),
            IsOnboarded = true,
            Enabled = true,
            BotJoinedAt = _timeProvider.GetUtcNow().UtcDateTime,
            User = user,
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishOnboardedAsync(channel, cancellationToken);

        return Result.Success(ToDto(channel));
    }

    /// <summary>
    /// Publishes <see cref="ChannelOnboardedEvent"/> so the auto-discovered onboarding seed handlers (rewards,
    /// moderator roster, memberships, subscriber/VIP standing, channel info, owner profile, event responses,
    /// banned-user import, bot mod-join, default commands, EventSub subscribe) run for this channel — the same
    /// event the Twitch-OAuth login path publishes, and the one <see cref="BackgroundServices.OnboardedChannelSeedBackfillService"/>
    /// re-fires at startup. Every handler is documented idempotent, so publishing on both a brand-new channel
    /// and a repaired (already-existing but re-onboarded) one is always safe.
    /// </summary>
    private Task PublishOnboardedAsync(Channel channel, CancellationToken cancellationToken) =>
        _eventBus.PublishAsync(
            new ChannelOnboardedEvent
            {
                BroadcasterId = channel.Id,
                OwnerUserId = channel.OwnerUserId,
                TwitchChannelId = channel.TwitchChannelId!,
                Name = channel.Name,
            },
            cancellationToken
        );

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<ChannelOverlayInfo?> GetByOverlayTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    )
    {
        return await _db
            .Channels.Include(c => c.User)
            .Where(c => c.OverlayToken == token)
            .Select(c => new ChannelOverlayInfo(c.Id.ToString(), c.User.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ChannelDto ToDto(Channel c) =>
        new(
            c.Id.ToString(),
            c.Name,
            c.User?.DisplayName ?? c.Name,
            c.User?.ProfileImageUrl,
            c.IsLive,
            c.IsOnboarded,
            c.Title,
            c.GameName,
            null,
            c.BotJoinedAt,
            "free",
            c.Language,
            c.CreatedAt
        );
}
