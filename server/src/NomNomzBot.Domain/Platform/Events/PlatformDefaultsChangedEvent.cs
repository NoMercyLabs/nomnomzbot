// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
