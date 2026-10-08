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
