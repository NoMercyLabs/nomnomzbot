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

namespace NomNomzBot.Infrastructure.Games.Frames;

/// <summary>
/// One player's outcome in a <see cref="GameResultsFrame"/>. Each game fills the fields it has: crash sends
/// <c>cashedAt</c> (null when the player busted); drop sends <c>landed</c>, <c>distance</c> (both null when the
/// player never dropped) and <c>won</c>, with no stake; heist sends <c>escaped</c>; raffle sends <c>won</c>.
/// </summary>
public sealed record GameResultRow
{
    public required string Player { get; init; }

    public long? Stake { get; init; }

    public double? CashedAt { get; init; }

    public double? Landed { get; init; }

    public double? Distance { get; init; }

    public bool? Won { get; init; }

    public bool? Escaped { get; init; }

    public required long Payout { get; init; }
}
