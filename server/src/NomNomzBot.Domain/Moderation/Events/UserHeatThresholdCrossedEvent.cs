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
    public required Guid SubjectUserId { get; init; }
    public required string SubjectTwitchUserId { get; init; }
    public required decimal HeatScore { get; init; }
    public required int Threshold { get; init; }
}
