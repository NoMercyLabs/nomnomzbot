// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// Serializes TTS synthesis + overlay dispatch per channel, one utterance at a time, in the ORDER
/// requests arrived — not the order their synthesis calls happen to finish. Two utterances requested
/// close together (two chat commands, a command overlapping a redemption) synthesize via async network
/// calls to the same or different providers, which do not resolve in call order; without this, whichever
/// one finishes first reaches the overlay first, so the audio queued at the widget can land out of order
/// against the chat messages the pipeline already sent — a viewer hears a DIFFERENT utterance than the
/// one that matches what just appeared in chat. Acquire before synthesizing/dispatching, release after.
/// </summary>
public interface ITtsChannelSerializer
{
    /// <summary>
    /// Waits for exclusive access to the given channel's TTS dispatch, then returns a handle that
    /// releases it on disposal. Await inside a <c>using</c>/<c>await using</c> block spanning exactly the
    /// synthesize-then-push work — never the upstream gating (enabled/cap/censor checks), which has no
    /// ordering requirement and would otherwise hold the lock far longer than necessary.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(Guid broadcasterId, CancellationToken ct = default);
}
