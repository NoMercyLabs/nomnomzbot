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

using System.Numerics;

namespace NomNomzBot.Infrastructure.Games.Catalog;

/// <summary>
/// The xoshiro256** generator, seeded by splitmix64 (drop-game.md section 2). Integer math only, so the
/// same 64-bit seed gives the same stream on every machine and in every language. Not for secrets: the
/// seed itself is drawn from the CSPRNG; this stream only replays a drop.
/// </summary>
public struct Xoshiro256StarStar
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public Xoshiro256StarStar(ulong seed)
    {
        ulong splitmix = seed;
        _s0 = SplitMix64(ref splitmix);
        _s1 = SplitMix64(ref splitmix);
        _s2 = SplitMix64(ref splitmix);
        _s3 = SplitMix64(ref splitmix);
    }

    public ulong Next()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5UL, 7) * 9UL;
        ulong shifted = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= shifted;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
