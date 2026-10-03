// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
