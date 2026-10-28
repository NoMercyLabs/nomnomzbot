// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>
/// Keeps the <c>ChannelModerators</c> roster (what the People page Moderators tab reads) in step with Twitch
/// while the bot runs. <c>channel.moderator.add</c> upserts the person for that channel and
/// <c>channel.moderator.remove</c> deletes that channel's row. The boot/onboarding sync
/// (<c>CommunityRosterService</c>) stays the catch-up path. VIPs have no stored roster (no table), so the
/// VIP events need no handler here. A failure is logged, never thrown, so the dashboard broadcast still runs.
/// </summary>
public sealed class ModeratorRosterLiveSyncHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ILogger<ModeratorRosterLiveSyncHandler> logger
) : IEventHandler<ModeratorAddedEvent>, IEventHandler<ModeratorRemovedEvent>
{
    public async Task HandleAsync(ModeratorAddedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            Guid userId = await GetOrCreateUserIdAsync(@event, ct);
            await UpsertModeratorAsync(@event.BroadcasterId, userId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Moderator roster live sync: add failed for {BroadcasterId} / {TwitchUserId}",
                @event.BroadcasterId,
                @event.UserId
            );
        }
    }

    public async Task HandleAsync(ModeratorRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            Guid? userId = await db
                .Users.Where(u => u.TwitchUserId == @event.UserId)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);
            if (userId is null)
                return;

            ChannelModerator? row = await db.ChannelModerators.FirstOrDefaultAsync(
                cm => cm.ChannelId == @event.BroadcasterId && cm.UserId == userId.Value,
                ct
            );
            if (row is null)
                return;

            db.ChannelModerators.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Moderator roster live sync: remove failed for {BroadcasterId} / {TwitchUserId}",
                @event.BroadcasterId,
                @event.UserId
            );
        }
    }

    private async Task<Guid> GetOrCreateUserIdAsync(
        ModeratorAddedEvent @event,
        CancellationToken ct
    )
    {
        Guid? existing = await db
            .Users.Where(u => u.TwitchUserId == @event.UserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing.Value;

        User user = new()
        {
            TwitchUserId = @event.UserId,
            Username = @event.UserLogin,
            UsernameNormalized = @event.UserLogin.ToLowerInvariant(),
            DisplayName = @event.UserDisplayName,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private async Task UpsertModeratorAsync(Guid channelId, Guid userId, CancellationToken ct)
    {
        // IgnoreQueryFilters: a removed moderator is only soft-deleted and still owns the (channel, user)
        // key, so a re-add must revive that row instead of colliding with it. Scoped by ChannelId.
        ChannelModerator? row = await db
            .ChannelModerators.IgnoreQueryFilters()
            .FirstOrDefaultAsync(cm => cm.ChannelId == channelId && cm.UserId == userId, ct);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (row is null)
            db.ChannelModerators.Add(
                new()
                {
                    ChannelId = channelId,
                    UserId = userId,
                    GrantedAt = now,
                }
            );
        else if (row.DeletedAt is not null)
        {
            row.DeletedAt = null;
            row.DeletedBy = null;
            row.GrantedAt = now;
        }
        else
            return;

        await db.SaveChangesAsync(ct);
    }
}
