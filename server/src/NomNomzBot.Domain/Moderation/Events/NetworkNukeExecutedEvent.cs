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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A network-nuke batch finished fanning out (moderation.md §2/§3.4). <c>ChannelCount</c> = channels
/// actually actioned (successful legs). <c>BroadcasterId</c> = the origin channel.
/// </summary>
public sealed class NetworkNukeExecutedEvent : DomainEventBase
{
    /// <summary>The internal id of this network nuke.</summary>
    public required Guid BatchId { get; init; }

    /// <summary>The internal id of the channel that started the nuke.</summary>
    public required Guid OriginBroadcasterId { get; init; }

    /// <summary>The internal id of the user who started the nuke.</summary>
    public required Guid InitiatedByUserId { get; init; }

    /// <summary>The Twitch user id of the viewer who was banned (a number as text).</summary>
    public required string TargetTwitchUserId { get; init; }

    /// <summary>How many channels the ban was applied in.</summary>
    public required int ChannelCount { get; init; }
}
