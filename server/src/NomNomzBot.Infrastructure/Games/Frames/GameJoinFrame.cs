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

    /// <summary>Crash only: <c>lobby</c> or <c>running</c>, the phase the player joined in.</summary>
    public string? Phase { get; init; }

    public int? CrewSize { get; init; }

    public double? SuccessChance { get; init; }

    public IReadOnlyList<GameRosterEntry>? Crew { get; init; }

    public long? Pot { get; init; }

    public IReadOnlyList<GameRosterEntry>? Entrants { get; init; }
}
