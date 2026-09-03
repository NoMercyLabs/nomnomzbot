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
/// An outgoing raid has been STARTED — the countdown is running, the viewers have not moved yet, and the
/// broadcaster can still cancel it.
///
/// <para>Observed from <c>channel.moderate</c>'s <c>raid</c> action, which Twitch emits the moment the raid
/// is initiated. This is the right event for anything that should run DURING the countdown — an outro
/// scene, a "we are raiding X" message, a goodbye overlay.</para>
///
/// <para>It is emphatically NOT the right event for ending the broadcast: doing that here kills the stream
/// at the start of the countdown instead of the end of it. Use <see cref="OutgoingRaidEvent"/>, which fires
/// once the raid has actually executed.</para>
/// </summary>
public sealed class OutgoingRaidStartedEvent : DomainEventBase
{
    /// <summary>The broadcaster the raid is aimed at.</summary>
    public required string ToUserId { get; init; }

    public required string ToDisplayName { get; init; }
    public required string ToLogin { get; init; }

    /// <summary>Viewers at the moment the raid was started — the final count can differ.</summary>
    public required int ViewerCount { get; init; }
}
