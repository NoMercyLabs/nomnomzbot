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
