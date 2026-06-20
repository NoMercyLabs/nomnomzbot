// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required int Bits { get; init; }

    /// <summary>How the Bits were used: <c>cheer</c> or <c>power_up</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The accompanying chat message text (EventSub nests this under <c>message.text</c>), or <c>null</c>.</summary>
    public string? MessageText { get; init; }
}
