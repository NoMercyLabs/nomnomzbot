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

/// <summary>
/// Published when a channel's stream goes online (EventSub stream.online).
/// </summary>
public sealed class ChannelOnlineEvent : DomainEventBase
{
    public required string BroadcasterDisplayName { get; init; }
    public required string StreamTitle { get; init; }
    public required string GameName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
