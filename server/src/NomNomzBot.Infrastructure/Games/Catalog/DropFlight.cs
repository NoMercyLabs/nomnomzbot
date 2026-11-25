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

namespace NomNomzBot.Infrastructure.Games.Catalog;

/// <summary>Where one drop enters and where it lands, both on the 0 to 100 track.</summary>
public readonly record struct DropFlightPath(double SpawnX, double LandingX);

/// <summary>
/// The server-seeded drop result (drop-game.md section 2). One 64-bit seed decides the whole flight:
/// the same seed, target, spread and boost always give the same path. The overlay only animates it.
/// Draw order is part of the contract: draw 1 is the spawn, draw 2 is the landing offset.
/// </summary>
public static class DropFlight
{
    private const double TrackLength = 100.0;
    private const ulong SpawnSteps = 1001UL;
    private const ulong OffsetSteps = 20001UL;
    private const double OffsetUnit = 10000.0;

    /// <param name="seed">The per-drop 64-bit seed.</param>
    /// <param name="target">The target position on the track.</param>
    /// <param name="spread">Half width of the landing range, in track units.</param>
    /// <param name="boost">0 to 1: pulls the landing toward the target (the luck plug-in point).</param>
    public static DropFlightPath Compute(ulong seed, double target, double spread, double boost)
    {
        Xoshiro256StarStar stream = new(seed);
        double spawnX = (stream.Next() % SpawnSteps) / 10.0;
        double offset = ((long)(stream.Next() % OffsetSteps) - 10000L) / OffsetUnit;
        double landing = target + offset * (1.0 - boost) * spread;
        double clamped = Math.Clamp(landing, 0.0, TrackLength);
        return new(spawnX, Math.Round(clamped, 1, MidpointRounding.AwayFromZero));
    }
}
