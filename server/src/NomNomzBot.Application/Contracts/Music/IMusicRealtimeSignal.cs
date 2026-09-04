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
/// The poller reads the provider's DOCUMENTED API and owns the correctness-of-record state shaping — dedupe,
/// change detection, per-action Can* permission flags. A realtime transport is free to ALSO publish its own
/// optimistic <c>PlaybackStateChangedEvent</c> straight from a frame the instant it arrives (S-MUSIC-1 —
/// <c>SpotifyDealerConnection</c> does exactly this, so the overlay never waits out a poll tick plus its own
/// interpolation for a track change) — but it must still <see cref="Nudge"/> the poller when it does. The
/// nudge is what re-baselines the poller's dedupe snapshot from the documented API on the very next pass, so a
/// later natural poll tick never re-publishes a stale-looking "change" against a baseline the undocumented
/// frame already moved past. As a trigger it cannot corrupt anything on its own — the worst a bad frame does
/// is cause one extra poll, and if the socket dies entirely the 1s timer still runs.
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
