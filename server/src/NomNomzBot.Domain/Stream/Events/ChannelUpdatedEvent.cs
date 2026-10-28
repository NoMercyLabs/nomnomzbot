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

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>When the channel title or category changes.</summary>
public sealed class ChannelUpdatedEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this channel update was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The name of the channel owner as shown in chat.</summary>
    public required string BroadcasterDisplayName { get; init; }

    /// <summary>The new stream title.</summary>
    public required string NewTitle { get; init; }

    /// <summary>The name of the new game or category.</summary>
    public required string NewGameName { get; init; }
}
