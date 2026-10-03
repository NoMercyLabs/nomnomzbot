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

/// <summary>When this channel gives another channel a shoutout.</summary>
public sealed class ShoutoutSentEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the channel that got the shoutout.</summary>
    public required string ToUserId { get; init; }

    /// <summary>The name of that channel as shown in chat.</summary>
    public required string ToDisplayName { get; init; }
}
