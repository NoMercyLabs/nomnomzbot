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
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// A moderator changed a chatter's suspicious-user flag on Twitch (<c>channel.suspicious_user.update</c>):
/// store the new flag so chat, the viewer card and the trust ladder see it, and push it to the dashboard.
/// </summary>
public sealed class SuspiciousUserUpdatedHandler(ILowTrustStatusService statuses)
    : IEventHandler<SuspiciousUserUpdatedEvent>
{
    public async Task HandleAsync(
        SuspiciousUserUpdatedEvent @event,
        CancellationToken ct = default
    ) =>
        await statuses.ApplyAsync(
            @event.BroadcasterId,
            @event.UserId,
            @event.LowTrustStatus,
            banEvasionEvaluation: null,
            ct
        );
}

/// <summary>
/// A flagged chatter spoke (<c>channel.suspicious_user.message</c>). The message carries the current flag, so it
/// is stored too, and the message goes to the moderation queue for a human. The message is never deleted here:
/// Twitch already hides a restricted chatter's messages from chat, and a monitored chatter's stay visible.
/// </summary>
public sealed class SuspiciousUserMessageHandler(
    ILowTrustStatusService statuses,
    IModerationQueueService queue
) : IEventHandler<SuspiciousUserMessageEvent>
{
    public async Task HandleAsync(SuspiciousUserMessageEvent @event, CancellationToken ct = default)
    {
        string status = LowTrustStatuses.Normalize(@event.LowTrustStatus);
        await statuses.ApplyAsync(
            @event.BroadcasterId,
            @event.UserId,
            status,
            @event.BanEvasionEvaluation,
            ct
        );

        if (status == LowTrustStatuses.None)
            return;

        await queue.EnqueueHeldMessageAsync(
            @event.BroadcasterId,
            @event.MessageId,
            @event.UserId,
            @event.UserDisplayName,
            @event.Text,
            status,
            ct,
            ModerationQueueSource.SuspiciousUser
        );
    }
}
