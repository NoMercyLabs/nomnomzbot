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

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>When another channel raids this channel.</summary>
public sealed class RaidReceivedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the channel that sent the raid.</summary>
    public required string FromUserId { get; init; }

    /// <summary>The name of the raiding channel as shown in chat.</summary>
    public required string FromDisplayName { get; init; }

    /// <summary>How many viewers came with the raid.</summary>
    public required int ViewerCount { get; init; }
}
