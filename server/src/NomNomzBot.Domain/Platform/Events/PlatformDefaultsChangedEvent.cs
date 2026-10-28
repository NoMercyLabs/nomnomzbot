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

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// An operator saved one platform default. Platform-scoped, so the inherited <c>BroadcasterId</c> stays
/// <c>Guid.Empty</c>. The operator hub pushes it so every open platform defaults page reloads.
/// </summary>
public sealed class PlatformDefaultsChangedEvent : DomainEventBase
{
    /// <summary><c>actions</c> | <c>builtin-replies</c> | <c>event-responses</c> | <c>tts-voice</c>.</summary>
    public required string Set { get; init; }

    /// <summary>The row that was saved (action key, <c>builtinKey/slot</c>, event type); null for the single TTS voice default.</summary>
    public string? Key { get; init; }
}
