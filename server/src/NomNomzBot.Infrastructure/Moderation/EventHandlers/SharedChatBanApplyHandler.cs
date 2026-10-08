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
/// enforced in the service, never here). Best-effort per partner — one failure never blocks the rest, and every
/// ban that is refused or skipped is already reported to that partner's streamer by the service. After a restart
/// the tracker is empty, so the origin's session is restored from Helix first and its participants adopted.
/// </summary>
public sealed class SharedChatBanApplyHandler(
    ISharedBanService sharedBans,
    ISharedChatSessionTracker sessions,
    ISharedChatSessionRestorer restorer,
    ILogger<SharedChatBanApplyHandler> logger
) : IEventHandler<SharedChatBanIssuedEvent>
{
    public async Task HandleAsync(SharedChatBanIssuedEvent @event, CancellationToken ct = default)
    {
        SharedChatSessionInfo? origin = await restorer.EnsureActiveSessionAsync(
            @event.OriginChannelId,
            ct
        );
        if (origin?.SessionId == @event.SharedChatSessionId)
            await restorer.AdoptParticipantsAsync(origin, ct);

        IReadOnlyList<Guid> candidates = sessions.GetChannelsInSession(@event.SharedChatSessionId);
        foreach (Guid partner in candidates)
        {
            if (partner == @event.OriginChannelId)
                continue;

            Result<SharedBanApplicationResult> outcome =
                await sharedBans.ApplyInboundSharedBanAsync(partner, @event, ct);
            if (outcome.IsFailure)
                logger.LogWarning(
                    "Shared ban not applied for partner {Partner} (origin {Origin}): {Reason} - {Error}",
                    partner,
                    @event.OriginChannelId,
                    outcome.ErrorCode,
                    outcome.ErrorMessage
                );
            else
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
