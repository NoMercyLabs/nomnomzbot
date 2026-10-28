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
