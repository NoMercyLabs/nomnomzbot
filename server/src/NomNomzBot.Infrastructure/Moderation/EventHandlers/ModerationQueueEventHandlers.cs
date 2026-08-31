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
/// Feeds the moderation review queue (moderation.md J.1) from AutoMod's own hold/resolve events — the backend
/// half of S066's "a mod approves a held message from the dashboard". A hold enqueues a pending row; an update
/// closes it when Twitch reports the message was resolved OUTSIDE this dashboard (another moderator, or Twitch
/// auto-expiry) — a dashboard-initiated resolve already closes its own row directly in
/// <see cref="IModerationQueueService.ResolveAsync"/> and does not re-enter here.
/// </summary>
public sealed class AutoModMessageHeldQueueHandler(IModerationQueueService queue)
    : IEventHandler<AutoModMessageHeldEvent>
{
    public async Task HandleAsync(AutoModMessageHeldEvent @event, CancellationToken ct = default) =>
        await queue.EnqueueHeldMessageAsync(
            @event.BroadcasterId,
            @event.MessageId,
            @event.UserId,
            @event.UserDisplayName,
            @event.Text,
            @event.Category,
            ct
        );
}

public sealed class AutoModMessageUpdatedQueueHandler(IModerationQueueService queue)
    : IEventHandler<AutoModMessageUpdatedEvent>
{
    public Task HandleAsync(AutoModMessageUpdatedEvent @event, CancellationToken ct = default) =>
        queue.ApplyExternalResolutionAsync(
            @event.BroadcasterId,
            @event.MessageId,
            @event.Status,
            ct
        );
}
