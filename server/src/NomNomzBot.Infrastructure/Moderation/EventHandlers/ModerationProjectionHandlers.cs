// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Feed the J.4/J.5 projections (moderation.md §3.8) from the moderation facts the bus already carries —
/// EventSub-translated, so EVERY Twitch-side action counts, not only bot-issued ones. Heat deltas per §3.8:
/// ban +40, timeout +15, AutoMod +5; warns shape the rollup only. (The filter-hit and validated-report
/// deltas wire in the same way once their events exist.)
/// </summary>
public sealed class UserBannedProjectionHandler(IModerationProjectionService projections)
    : IEventHandler<UserBannedEvent>
{
    public Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default) =>
        projections.ApplyActionAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            "ban",
            @event.OccurredAt.UtcDateTime,
            ct
        );
}

/// <summary>The timeout leg (+15 heat).</summary>
public sealed class UserTimedOutProjectionHandler(IModerationProjectionService projections)
    : IEventHandler<UserTimedOutEvent>
{
    public Task HandleAsync(UserTimedOutEvent @event, CancellationToken ct = default) =>
        projections.ApplyActionAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            "timeout",
            @event.OccurredAt.UtcDateTime,
            ct
        );
}

/// <summary>The unban leg — rollup bookkeeping only (LastAction), no heat.</summary>
public sealed class UserUnbannedProjectionHandler(IModerationProjectionService projections)
    : IEventHandler<UserUnbannedEvent>
{
    public Task HandleAsync(UserUnbannedEvent @event, CancellationToken ct = default) =>
        projections.ApplyActionAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            "unban",
            @event.OccurredAt.UtcDateTime,
            ct
        );
}

/// <summary>The warn leg — counts in the rollup, no heat delta (§3.8 lists none for warnings).</summary>
public sealed class WarningSentProjectionHandler(IModerationProjectionService projections)
    : IEventHandler<WarningSentEvent>
{
    public Task HandleAsync(WarningSentEvent @event, CancellationToken ct = default) =>
        projections.ApplyActionAsync(
            @event.BroadcasterId,
            @event.UserId,
            "warn",
            @event.OccurredAt.UtcDateTime,
            ct
        );
}

/// <summary>The AutoMod leg (+5 heat) — a message AutoMod held/flagged for this user.</summary>
public sealed class MessageAutoModdedProjectionHandler(IModerationProjectionService projections)
    : IEventHandler<MessageAutoModdedEvent>
{
    public Task HandleAsync(MessageAutoModdedEvent @event, CancellationToken ct = default) =>
        projections.ApplyActionAsync(
            @event.BroadcasterId,
            @event.UserId,
            "automod_denied",
            @event.OccurredAt.UtcDateTime,
            ct
        );
}
