// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Events;

using Platform;

/// <summary>When the bot joins a channel.</summary>
public sealed class ChannelJoinedEvent : DomainEventBase
{
    /// <summary>The id of the channel the bot joined.</summary>
    public required string ChannelId { get; init; }

    /// <summary>The name of the channel the bot joined.</summary>
    public required string ChannelName { get; init; }
}
