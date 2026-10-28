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

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Re-sends the overlay alert payloads captured when an activity event first fired, byte for byte, to every
/// widget that subscribes to them now. Never re-derives a payload, so it can never re-run what built it.
/// </summary>
public interface IRenderedAlertReplayer
{
    /// <param name="broadcasterId">The tenant channel.</param>
    /// <param name="channelEventId">The activity-feed event whose captures are re-sent.</param>
    /// <param name="includeTts">False when the event's configured response is being replayed too — that
    /// response queues its own TTS, so the captured utterance would play twice.</param>
    /// <param name="ct">Cancels the lookup and the pushes.</param>
    /// <returns>How many widget pushes were made.</returns>
    Task<int> ResendAsync(
        Guid broadcasterId,
        string channelEventId,
        bool includeTts,
        CancellationToken ct = default
    );
}
