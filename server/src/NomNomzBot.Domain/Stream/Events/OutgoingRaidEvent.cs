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

    /// <summary>The name of the raided channel as shown in chat.</summary>
    public required string ToDisplayName { get; init; }

    /// <summary>The login name of the raided channel, in lowercase.</summary>
    public required string ToLogin { get; init; }

    /// <summary>How many viewers the raid sends.</summary>
    public required int ViewerCount { get; init; }
}
