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
