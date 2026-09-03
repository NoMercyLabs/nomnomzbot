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
/// An OUTGOING raid that has ACTUALLY HAPPENED — this channel's viewers have moved to another broadcaster.
///
/// <para>Raised from the <c>channel.raid</c> subscription keyed on <c>from_broadcaster_user_id</c>, which
/// Twitch sends once the raid executes. Drives the <c>channel.raid.out</c> event responses.</para>
///
/// <para><b>Not to be confused with <see cref="OutgoingRaidStartedEvent"/>.</b> That one fires when the
/// raid is INITIATED and the countdown begins, and it used to be the source of this event — which is why
/// a raid pipeline that stops the stream ended the broadcast at the start of the countdown, taking the
/// countdown, the outro and the viewers with it. Anything that must not happen until the viewers have
/// actually left belongs on THIS event.</para>
/// </summary>
public sealed class OutgoingRaidEvent : DomainEventBase
{
    /// <summary>The raided (target) broadcaster's Twitch user id.</summary>
    public required string ToUserId { get; init; }

    public required string ToDisplayName { get; init; }
    public required string ToLogin { get; init; }
    public required int ViewerCount { get; init; }
}
