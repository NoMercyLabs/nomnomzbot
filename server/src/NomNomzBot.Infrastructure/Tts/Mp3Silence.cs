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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Silent MP3 frames in the format the Edge read-aloud service answers with (MPEG-2 Layer III, 24 kHz, mono,
/// 48 kbit/s), for a pause between two clips that are joined into one. A frame whose body is all zero decodes
/// to silence (a join of two real clips around 25 of them decoded cleanly in ffmpeg, 2026-11-11).
/// </summary>
internal static class Mp3Silence
{
    private const int FrameBytes = 144;
    private const int FrameMs = 24;
    private static readonly byte[] Header = [0xFF, 0xF3, 0x64, 0xC4];

    /// <summary>Whole frames that last at least <paramref name="milliseconds"/>; empty for zero or less.</summary>
    internal static byte[] OfMilliseconds(int milliseconds)
    {
        if (milliseconds <= 0)
            return [];

        int frames = (milliseconds + FrameMs - 1) / FrameMs;
        byte[] silence = new byte[frames * FrameBytes];
        for (int i = 0; i < frames; i++)
            Header.CopyTo(silence, i * FrameBytes);

        return silence;
    }
}
