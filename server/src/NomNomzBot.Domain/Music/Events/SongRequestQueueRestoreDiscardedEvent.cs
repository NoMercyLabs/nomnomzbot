// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// Published once per channel, on startup, when that channel's persisted song-request queue existed
/// but was too stale to trust (S001b's freshness rule) and was discarded instead of restored. The
/// channel is told rather than silently starting empty: a dashboard/chat handler can surface this as
/// "your pending song requests from before the last restart could not be recovered" the moment one
/// subscribes, and it is always logged as a warning at the point of discard regardless.
/// </summary>
public sealed class SongRequestQueueRestoreDiscardedEvent : DomainEventBase
{
    /// <summary>Human-readable reason, e.g. "queue was last touched 3d 4h ago (limit 4h)".</summary>
    public required string Reason { get; init; }
}
