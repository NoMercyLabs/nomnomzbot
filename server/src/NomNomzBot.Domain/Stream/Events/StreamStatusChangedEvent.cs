// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Stream.Events;

using Platform;

/// <summary>When the channel goes live or goes offline.</summary>
public sealed class StreamStatusChangedEvent : DomainEventBase
{
    /// <summary>The id of the channel whose status changed.</summary>
    public required string ChannelId { get; init; }

    /// <summary>True when the channel is live now, and false when it is offline.</summary>
    public required bool IsLive { get; init; }

    /// <summary>The id of the live stream. Empty when the channel is offline.</summary>
    public required string? StreamId { get; init; }
}
