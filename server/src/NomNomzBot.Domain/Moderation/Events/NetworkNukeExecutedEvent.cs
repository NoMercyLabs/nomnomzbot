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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A network-nuke batch finished fanning out (moderation.md §2/§3.4). <c>ChannelCount</c> = channels
/// actually actioned (successful legs). <c>BroadcasterId</c> = the origin channel.
/// </summary>
public sealed class NetworkNukeExecutedEvent : DomainEventBase
{
    public required Guid BatchId { get; init; }
    public required Guid OriginBroadcasterId { get; init; }
    public required Guid InitiatedByUserId { get; init; }
    public required string TargetTwitchUserId { get; init; }
    public required int ChannelCount { get; init; }
}
