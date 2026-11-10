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

/// <summary>A drop-game player landed their marker.</summary>
public sealed record GameDropFrame
{
    [WireLiteral("drop")]
    public string Kind { get; init; } = "drop";

    public required string Player { get; init; }

    public required double Landed { get; init; }

    public required double Distance { get; init; }

    public required bool Hit { get; init; }

    /// <summary>0 to 100: 100 on the target center, 0 at the win radius edge or beyond.</summary>
    public required int Score { get; init; }

    /// <summary>The 64-bit flight seed as a decimal string; the overlay draws the same sway from it everywhere.</summary>
    public required string Seed { get; init; }

    /// <summary>Where the item enters at the top, 0 to 100; the flight still ends on <see cref="Landed"/>.</summary>
    public required double SpawnX { get; init; }
}
