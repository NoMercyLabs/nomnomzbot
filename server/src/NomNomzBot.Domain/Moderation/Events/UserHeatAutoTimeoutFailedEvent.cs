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
/// The channel turned on automatic timeouts for heat, a viewer crossed the threshold, and the platform
/// refused the timeout. Nobody was timed out, so a human has to act. Published so the action-required
/// inbox can show it (the journal keeps it).
/// </summary>
public sealed class UserHeatAutoTimeoutFailedEvent : DomainEventBase
{
    /// <summary>The internal id of the viewer who should have been timed out.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>The Twitch user id of that viewer (a number as text).</summary>
    public required string SubjectTwitchUserId { get; init; }

    /// <summary>The viewer's name, when it is known.</summary>
    public string? SubjectUsername { get; init; }

    /// <summary>Why the platform call failed, as the platform reported it.</summary>
    public required string Error { get; init; }

    /// <summary>The viewer's heat score at the crossing, from 0 to 100.</summary>
    public required decimal HeatScore { get; init; }

    /// <summary>The heat score the channel set as its limit, from 0 to 100.</summary>
    public required int Threshold { get; init; }
}
