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

namespace NomNomzBot.Infrastructure.Games.Frames;

/// <summary>
/// A player bought in. Crash adds the multiplier they ride from; heist the crew so far and its odds; raffle the
/// pot and the entrants so far.
/// </summary>
public sealed record GameJoinFrame
{
    [WireLiteral("join")]
    public string Kind { get; init; } = "join";

    public required string Player { get; init; }

    public required long Stake { get; init; }

    public double? Entry { get; init; }

    public int? CrewSize { get; init; }

    public double? SuccessChance { get; init; }

    public IReadOnlyList<GameRosterEntry>? Crew { get; init; }

    public long? Pot { get; init; }

    public IReadOnlyList<GameRosterEntry>? Entrants { get; init; }
}
