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
/// An OUTGOING raid — this channel sending its viewers to another broadcaster. Twitch's <c>channel.raid</c>
/// subscription is keyed on <c>to_broadcaster_user_id</c> (incoming only, → <see cref="RaidEvent"/>), so the
/// outgoing direction is observed from <c>channel.moderate</c>'s <c>raid</c> action, whose nested detail names
/// the target channel and viewer count. Drives the <c>channel.raid.out</c> event responses.
/// </summary>
public sealed class OutgoingRaidEvent : DomainEventBase
{
    /// <summary>The raided (target) broadcaster's Twitch user id.</summary>
    public required string ToUserId { get; init; }

    public required string ToDisplayName { get; init; }
    public required string ToLogin { get; init; }
    public required int ViewerCount { get; init; }
}
