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

/// <summary>When this channel sends a raid to another channel.</summary>
public sealed class RaidSentEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the channel that gets the raid.</summary>
    public required string ToUserId { get; init; }

    /// <summary>The name of the channel that gets the raid, as shown in chat.</summary>
    public required string ToDisplayName { get; init; }
}
