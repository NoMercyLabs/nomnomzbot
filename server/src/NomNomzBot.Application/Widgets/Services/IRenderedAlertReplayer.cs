// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
