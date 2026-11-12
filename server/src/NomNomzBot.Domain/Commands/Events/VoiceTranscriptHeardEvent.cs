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

namespace NomNomzBot.Domain.Commands.Events;

/// <summary>
/// Published for every finished sentence the voice-listener page heard the streamer say, whether or not a
/// voice trigger matched and whether or not a stream is live. It lets a paired automation client react to
/// what the streamer says out loud.
/// </summary>
public sealed class VoiceTranscriptHeardEvent : DomainEventBase
{
    /// <summary>The sentence as the speech input wrote it, trimmed.</summary>
    public required string Text { get; init; }
}
