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

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published when this channel receives a raid.
/// </summary>
public sealed class RaidEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the channel that sent the raid.</summary>
    public required string FromUserId { get; init; }

    /// <summary>The name of the raiding channel as shown in chat.</summary>
    public required string FromDisplayName { get; init; }

    /// <summary>The login name of the raiding channel, in lowercase.</summary>
    public required string FromLogin { get; init; }

    /// <summary>How many viewers came with the raid.</summary>
    public required int ViewerCount { get; init; }
}
