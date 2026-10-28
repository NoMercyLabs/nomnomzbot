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

/// <summary>A crash player cashed out at the current multiplier.</summary>
public sealed record GameCashoutFrame
{
    [WireLiteral("cashout")]
    public string Kind { get; init; } = "cashout";

    public required string Player { get; init; }

    public required double Multiplier { get; init; }

    public required long Payout { get; init; }

    public required int CrewSize { get; init; }
}
