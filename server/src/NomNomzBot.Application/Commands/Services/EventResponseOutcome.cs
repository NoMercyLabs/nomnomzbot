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
