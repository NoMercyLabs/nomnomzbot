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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Restores the shared-chat session from Helix (Get Shared Chat Session, app token) so a restart does not lose
/// it. A channel that is in no session (Twitch answers not-found) stays untracked; a Helix failure is logged and
/// reads as "no session" — the ban path then reports it as such instead of crashing.
/// </summary>
public sealed class SharedChatSessionRestorer(
    ISharedChatSessionTracker tracker,
    ITwitchChatAssetsApi helix,
    IApplicationDbContext db,
    ILogger<SharedChatSessionRestorer> logger
) : ISharedChatSessionRestorer
{
    public async Task<SharedChatSessionInfo?> EnsureActiveSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SharedChatSessionInfo? tracked = tracker.GetActiveSession(broadcasterId);
        if (tracked is not null)
            return tracked;

        Result<TwitchSharedChatSession> reported = await helix.GetSharedChatSessionAsync(
            broadcasterId,
            ct
        );
        if (reported.IsFailure)
        {
            if (reported.ErrorCode != TwitchErrorCodes.NotFound)
                logger.LogWarning(
                    "Could not restore the shared-chat session for {Channel}: {Code} {Error}",
                    broadcasterId,
                    reported.ErrorCode,
                    reported.ErrorMessage
                );
            return null;
        }

        SharedChatSessionInfo session = new(
            reported.Value.SessionId,
            reported.Value.HostBroadcasterId,
            [.. reported.Value.Participants.Select(p => p.BroadcasterId)]
        );
        tracker.SetSession(broadcasterId, session);
        await AdoptParticipantsAsync(session, ct);
        logger.LogInformation(
            "Restored shared-chat session {Session} for {Channel} from Twitch",
            session.SessionId,
            broadcasterId
        );
        return session;
    }

    public async Task AdoptParticipantsAsync(
        SharedChatSessionInfo session,
        CancellationToken ct = default
    )
    {
        List<string> twitchIds = [.. session.ParticipantTwitchIds];
        List<Guid> localChannels = await db
            .Channels.AsNoTracking()
            .Where(c => c.TwitchChannelId != null && twitchIds.Contains(c.TwitchChannelId))
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (Guid channelId in localChannels)
        {
            if (tracker.GetActiveSession(channelId) is null)
                tracker.SetSession(channelId, session);
        }
    }
}
