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
/// A viewer's heat (J.5, 0–100 recent-violation pressure) crossed the channel's configured threshold
/// UPWARD (moderation.md §3.8) — fires only on the crossing, never while heat stays above it.
/// </summary>
public sealed class UserHeatThresholdCrossedEvent : DomainEventBase
{
    /// <summary>The internal id of the viewer whose heat went up.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>The viewer's id on <see cref="SubjectProvider"/> (a Twitch user id when that is Twitch).</summary>
    public required string SubjectTwitchUserId { get; init; }

    /// <summary>The platform the viewer's heat came from, such as <c>twitch</c>, <c>kick</c> or <c>youtube</c>.</summary>
    public string SubjectProvider { get; init; } = "twitch";

    /// <summary>The viewer's heat score now, from 0 to 100.</summary>
    public required decimal HeatScore { get; init; }

    /// <summary>The heat score the channel set as its limit, from 0 to 100.</summary>
    public required int Threshold { get; init; }
}
