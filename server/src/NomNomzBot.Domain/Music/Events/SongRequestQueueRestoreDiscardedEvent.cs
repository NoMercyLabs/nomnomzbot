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
