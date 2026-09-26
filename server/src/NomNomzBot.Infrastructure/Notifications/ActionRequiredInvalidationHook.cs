// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Infrastructure.Notifications;

/// <summary>
/// Turns every journaled event that can change a channel's action-required inbox into a live invalidation.
/// Each <see cref="IActionRequiredSource"/> names the event types that affect it, so adding a producer never
/// needs a new hub handler; the notifier coalesces bursts per channel. Platform-wide events (no channel) are
/// ignored — the inbox is per channel.
/// </summary>
public sealed class ActionRequiredInvalidationHook(
    IEnumerable<IActionRequiredSource> sources,
    IActionRequiredChangeNotifier notifier
) : IJournalPostCommitHook
{
    public Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    )
    {
        if (
            committed.BroadcasterId is { } channelId
            && channelId != Guid.Empty
            && sources.Any(s => s.InvalidatingEventTypes.Contains(committed.EventType))
        )
            notifier.NotifyChanged(channelId);

        return Task.FromResult(Result.Success());
    }
}
