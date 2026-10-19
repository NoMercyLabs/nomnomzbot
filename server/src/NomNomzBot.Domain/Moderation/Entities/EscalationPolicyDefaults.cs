// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// The one place each escalation-policy default and limit is written. A channel with no saved policy row
/// resolves to these values; the entity, the EF mapping, the service, the API request and the runtime
/// fallbacks all read them.
/// </summary>
public static class EscalationPolicyDefaults
{
    /// <summary>Ships OFF: a channel must opt in before the ladder punishes anybody.</summary>
    public const bool IsEnabled = false;

    public const bool CountAutoModViolations = false;

    /// <summary>The decaying offense window: a tally older than this restarts at offense 1 (one week).</summary>
    public const int OffenseWindowHours = 168;

    /// <summary>Longest accepted offense window: one year.</summary>
    public const int MaxOffenseWindowHours = 8760;

    /// <summary>The length, in seconds, a one-click chat Timeout uses.</summary>
    public const int DefaultTimeoutSeconds = 600;

    /// <summary>Twitch's longest timeout: 14 days.</summary>
    public const int MaxTimeoutSeconds = 1_209_600;

    /// <summary>Most rungs one ladder may hold.</summary>
    public const int MaxLadderSteps = 50;

    /// <summary>
    /// The default ladder: offense 1 warns, 2 to 5 time out for growing lengths, 6 and up ban (the highest
    /// step clamps). Action is warn, timeout or ban; the seconds are null unless the action is timeout.
    /// </summary>
    public static readonly IReadOnlyList<(
        int AtOffense,
        string Action,
        int? TimeoutSeconds
    )> Ladder =
    [
        (1, "warn", null),
        (2, "timeout", 60),
        (3, "timeout", 600),
        (4, "timeout", 3600),
        (5, "timeout", 86400),
        (6, "ban", null),
    ];
}
