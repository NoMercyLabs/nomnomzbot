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
/// A player bought in. The raffle adds the pot and the entrants so far.
/// </summary>
public sealed record GameJoinFrame
{
    [WireLiteral("join")]
    public string Kind { get; init; } = "join";

    public required string Player { get; init; }

    public required long Stake { get; init; }

    public long? Pot { get; init; }

    public IReadOnlyList<GameRosterEntry>? Entrants { get; init; }
}
