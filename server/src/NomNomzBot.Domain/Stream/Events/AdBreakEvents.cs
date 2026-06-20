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
/// Published when a mid-roll ad break starts (EventSub <c>channel.ad_break.begin</c>). Carries the break length
/// and whether Twitch scheduled it automatically; the requester fields are present only for a manually started
/// break. Inherits EventId / Timestamp / BroadcasterId from DomainEventBase (publisher sets the resolved tenant).
/// </summary>
public sealed class AdBreakBeganEvent : DomainEventBase
{
    public required int DurationSeconds { get; init; }
    public required bool IsAutomatic { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The user who started the break, or <c>null</c> for an automatic break.</summary>
    public string? RequesterUserId { get; init; }

    /// <summary>The display name of the user who started the break, or <c>null</c> for an automatic break.</summary>
    public string? RequesterDisplayName { get; init; }
}
