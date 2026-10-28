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

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when Bits are used on the channel (EventSub <c>channel.bits.use</c> — the unified Bits event
/// covering both cheers and Power-up redemptions). <see cref="Type"/> distinguishes the two (<c>cheer</c> vs
/// <c>power_up</c>); <see cref="MessageText"/> is the accompanying chat text when present. Inherits EventId /
/// Timestamp / BroadcasterId from DomainEventBase (publisher sets the resolved tenant).
/// </summary>
public sealed class BitsUsedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the viewer who used the Bits (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The viewer's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>How many Bits were used.</summary>
    public required int Bits { get; init; }

    /// <summary>How the Bits were used: <c>cheer</c> or <c>power_up</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The accompanying chat message text (EventSub nests this under <c>message.text</c>), or <c>null</c>.</summary>
    public string? MessageText { get; init; }

    /// <summary>
    /// The Power-up kind for a <c>power_up</c> use (<c>message_effect</c>, <c>celebration</c> or
    /// <c>gigantify_an_emote</c>, from <c>power_up.type</c>), or <c>null</c> for a cheer.
    /// </summary>
    public string? PowerUpType { get; init; }
}
