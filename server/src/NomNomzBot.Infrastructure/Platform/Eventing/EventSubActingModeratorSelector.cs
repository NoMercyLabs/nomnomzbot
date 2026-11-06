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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>The moderator whose own Twitch grant authorizes the system's subscriptions on a channel.</summary>
public sealed record EventSubActingModerator(Guid UserId, string TwitchUserId, ManagementRole Role);

/// <summary>
/// Picks who authorizes the system's EventSub subscriptions on a channel that has no broadcaster token: the
/// highest-ranked team member who holds their own live Twitch connection. The system uses the highest authority
/// it holds; a person who acts in the dashboard is judged by their own role and is never raised by this pick
/// (Stoney, 2026-11-06). Onboarding the channel replaces it with the broadcaster's own token.
/// </summary>
public interface IEventSubActingModeratorSelector
{
    /// <summary>Null when the channel is onboarded (the broadcaster authorizes) or nobody eligible is connected.</summary>
    Task<EventSubActingModerator?> SelectAsync(Guid channelId, CancellationToken ct = default);
}

/// <inheritdoc cref="IEventSubActingModeratorSelector" />
public sealed class EventSubActingModeratorSelector(IApplicationDbContext db)
    : IEventSubActingModeratorSelector
{
    // Highest authority first. ManagementRole is stored as a string, so the rank is applied here, never in SQL.
    private static readonly ManagementRole[] ByAuthority =
    [
        ManagementRole.Broadcaster,
        ManagementRole.Editor,
        ManagementRole.LeadModerator,
        ManagementRole.Moderator,
    ];

    public async Task<EventSubActingModerator?> SelectAsync(
        Guid channelId,
        CancellationToken ct = default
    )
    {
        bool? onboarded = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => (bool?)c.IsOnboarded)
            .FirstOrDefaultAsync(ct);
        if (onboarded is null or true)
            return null;

        // IgnoreQueryFilters: the memberships and connections belong to the moderated channel and to the
        // moderators' own accounts, not to the request's tenant.
        var candidates = await (
            from membership in db.ChannelMemberships.IgnoreQueryFilters()
            where membership.BroadcasterId == channelId && membership.DeletedAt == null
            join user in db.Users on membership.UserId equals user.Id
            where user.TwitchUserId != null
            join connection in db.IntegrationConnections.IgnoreQueryFilters()
                on user.TwitchUserId equals connection.ProviderAccountId
            where
                connection.Provider == AuthEnums.IntegrationProvider.Twitch
                && connection.Status == AuthEnums.IntegrationStatus.Connected
                && connection.DeletedAt == null
            select new
            {
                user.Id,
                TwitchUserId = user.TwitchUserId!,
                membership.ManagementRole,
                connection.LastRefreshedAt,
            }
        ).ToListAsync(ct);

        return candidates
            .OrderBy(c => Array.IndexOf(ByAuthority, c.ManagementRole))
            .ThenByDescending(c => c.LastRefreshedAt)
            .Select(c => new EventSubActingModerator(c.Id, c.TwitchUserId, c.ManagementRole))
            .FirstOrDefault();
    }
}
