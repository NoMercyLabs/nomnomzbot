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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Infrastructure.Overlays;

/// <summary>
/// The generic overlay event feed's source: one post-commit hook that fans EVERY journaled event out to the
/// channel's connected overlays via <see cref="IOverlayEventFeed"/> (widgets-overlays.md). Because it rides the
/// single "sees every event" seam, a custom overlay receives the whole event stream — chat, alerts, now-playing,
/// custom events — without one handler per type. Encrypted payloads and tenant-less events are skipped (an overlay
/// only ever gets its own channel's non-sensitive events, over the token-gated overlay group).
/// </summary>
public sealed class OverlayEventFeedHook(
    IOverlayEventFeed feed,
    ILogger<OverlayEventFeedHook> logger
) : IJournalPostCommitHook
{
    public async Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    )
    {
        if (committed.BroadcasterId is not Guid broadcasterId || broadcasterId == Guid.Empty)
            return Result.Success();

        // Never push an encrypted payload to a browser source — the overlay could not use it and it must not leave
        // the vault boundary in the clear.
        if (committed.PayloadIsEncrypted)
            return Result.Success();

        try
        {
            await feed.BroadcastEventAsync(
                broadcasterId,
                committed.EventType,
                committed.PayloadJson,
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort delivery: a hub push failure never rolls back the commit or blocks other hooks.
            logger.LogWarning(
                ex,
                "Overlay feed push failed for {EventType} ({EventId})",
                committed.EventType,
                committed.EventId
            );
        }

        return Result.Success();
    }
}
