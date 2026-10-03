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
/// The round resolved. Each game adds its own summary next to <c>results</c>: crash where it crashed, drop the
/// target and radius, heist the odds and crew size, raffle the pot and the winner.
/// </summary>
public sealed record GameResultsFrame
{
    [WireLiteral("results")]
    public string Kind { get; init; } = "results";

    public required IReadOnlyList<GameResultRow> Results { get; init; }

    public double? CrashedAt { get; init; }

    public bool? Capped { get; init; }

    public double? Target { get; init; }

    public double? Radius { get; init; }

    public double? SuccessChance { get; init; }

    public int? CrewSize { get; init; }

    public long? Pot { get; init; }

    public string? Winner { get; init; }

    public long? Payout { get; init; }
}
