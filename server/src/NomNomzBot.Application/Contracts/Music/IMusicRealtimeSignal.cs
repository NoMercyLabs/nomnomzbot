// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>
/// A "playback just changed, do not wait for the next tick" signal between a provider's realtime transport
/// and <c>MusicStatePollingService</c>.
///
/// <para>
/// The poller reads the provider's DOCUMENTED API and owns all the state shaping — dedupe, change detection,
/// the <c>PlaybackStateChangedEvent</c> shape. A realtime transport only says WHEN to look. Keeping it to a
/// nudge is deliberate: Spotify's realtime frames come off an undocumented endpoint whose payload shape can
/// change under us, and parsing that into playback state would make an undocumented wire format the source of
/// truth for what the overlay shows. As a trigger it cannot corrupt anything — the worst a bad frame does is
/// cause one extra poll, and if the socket dies entirely the 1s timer still runs.
/// </para>
/// </summary>
public interface IMusicRealtimeSignal
{
    /// <summary>
    /// Wake the poller now. Safe to call from any thread, at any rate, whether or not anyone is listening —
    /// a nudge with no waiter is remembered, so a signal that lands mid-poll still forces the next pass.
    /// </summary>
    void Nudge(Guid broadcasterId);

    /// <summary>
    /// Completes when someone calls <see cref="Nudge"/>, or when [cancellationToken] fires. The caller races
    /// this against its own timer, so this must never complete on its own.
    /// </summary>
    Task WaitForNudgeAsync(CancellationToken cancellationToken);
}
