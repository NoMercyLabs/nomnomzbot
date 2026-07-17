// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// The INBOUND half of the shared-ban trust web (moderation.md §3.5): fans an offered shared-chat ban out
/// to every OTHER local channel in the same session, each of which decides for itself through
/// <see cref="ISharedBanService.ApplyInboundSharedBanAsync"/> (accept + trust + same-session predicate is
/// enforced in the service, never here). Best-effort per partner — one failure never blocks the rest.
/// </summary>
public sealed class SharedChatBanApplyHandler(
    ISharedBanService sharedBans,
    ISharedChatSessionTracker sessions,
    ILogger<SharedChatBanApplyHandler> logger
) : IEventHandler<SharedChatBanIssuedEvent>
{
    public async Task HandleAsync(SharedChatBanIssuedEvent @event, CancellationToken ct = default)
    {
        IReadOnlyList<Guid> candidates = sessions.GetChannelsInSession(@event.SharedChatSessionId);
        foreach (Guid partner in candidates)
        {
            if (partner == @event.OriginChannelId)
                continue;

            Result<SharedBanApplicationResult> outcome =
                await sharedBans.ApplyInboundSharedBanAsync(partner, @event, ct);
            if (outcome.IsFailure)
                logger.LogWarning(
                    "Shared-ban apply errored for partner {Partner} (origin {Origin}): {Error}",
                    partner,
                    @event.OriginChannelId,
                    outcome.ErrorMessage
                );
            else if (outcome.Value.Applied)
                logger.LogInformation(
                    "Shared-ban applied: {Target} banned in {Partner} from trusted origin {Origin} (session {Session})",
                    @event.TargetTwitchUserId,
                    partner,
                    @event.OriginChannelId,
                    @event.SharedChatSessionId
                );
        }
    }
}
