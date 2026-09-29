// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// What one configured event response actually put in front of viewers: chat lines the bot sent, TTS
/// utterances a pipeline queued, and overlay-type responses pushed. Zero is a truthful outcome — a disabled
/// response, an empty template or a failed send all count nothing.
/// </summary>
public sealed record EventResponseOutcome(int ChatMessagesSent, int TtsQueued, int OverlaysShown)
{
    public static EventResponseOutcome None { get; } = new(0, 0, 0);

    public EventResponseOutcome Plus(EventResponseOutcome other) =>
        new(
            ChatMessagesSent + other.ChatMessagesSent,
            TtsQueued + other.TtsQueued,
            OverlaysShown + other.OverlaysShown
        );
}
