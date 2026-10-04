// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Reads the real length of an MP3 from its frame headers. Each Layer III frame holds a fixed number of samples
/// (1152 for MPEG-1, 576 for MPEG-2 and 2.5), so the sum of the frames divided by the sample rate is exact for
/// constant and variable bitrate alike. An ID3v2 tag at the start is skipped. When no frame is found, the length
/// falls back to the byte count at the bitrate the provider asked for.
/// </summary>
internal static class Mp3Duration
{
    // Bitrates in kbit/s for Layer III, by header index. Index 0 (free format) and 15 (bad) are invalid.
    private static readonly int[] Mpeg1Layer3Kbps =
    [
        0,
        32,
        40,
        48,
        56,
        64,
        80,
        96,
        112,
        128,
        160,
        192,
        224,
        256,
        320,
        0,
    ];
    private static readonly int[] Mpeg2Layer3Kbps =
    [
        0,
        8,
        16,
        24,
        32,
        40,
        48,
        56,
        64,
        80,
        96,
        112,
        128,
        144,
        160,
        0,
    ];

    // Sample rates in Hz by header index, for MPEG-1, MPEG-2 and MPEG-2.5. Index 3 is reserved.
    private static readonly int[] Mpeg1Rates = [44100, 48000, 32000, 0];
    private static readonly int[] Mpeg2Rates = [22050, 24000, 16000, 0];
    private static readonly int[] Mpeg25Rates = [11025, 12000, 8000, 0];

    /// <summary>The length of the audio in milliseconds.</summary>
    /// <param name="audio">The MP3 bytes.</param>
    /// <param name="requestedKbps">The bitrate asked for; used only when no frame header is readable.</param>
    public static int ToMilliseconds(byte[] audio, int requestedKbps)
    {
        double totalSeconds = 0;
        int frames = 0;
        int pos = SkipId3v2(audio);

        while (pos + 4 <= audio.Length)
        {
            if (!TryReadFrame(audio, pos, out int frameLength, out int samples, out int rate))
            {
                pos++;
                continue;
            }

            if (pos + frameLength > audio.Length)
                break;

            totalSeconds += (double)samples / rate;
            frames++;
            pos += frameLength;
        }

        if (frames > 0)
            return (int)Math.Round(totalSeconds * 1000.0, MidpointRounding.AwayFromZero);

        return (int)(audio.LongLength * 8L / Math.Max(1, requestedKbps));
    }

    private static int SkipId3v2(byte[] audio)
    {
        if (audio.Length < 10 || audio[0] != 'I' || audio[1] != 'D' || audio[2] != '3')
            return 0;

        int size =
            ((audio[6] & 0x7F) << 21)
            | ((audio[7] & 0x7F) << 14)
            | ((audio[8] & 0x7F) << 7)
            | (audio[9] & 0x7F);
        bool hasFooter = (audio[5] & 0x10) != 0;
        return Math.Min(audio.Length, 10 + size + (hasFooter ? 10 : 0));
    }

    private static bool TryReadFrame(
        byte[] audio,
        int pos,
        out int frameLength,
        out int samples,
        out int rate
    )
    {
        frameLength = 0;
        samples = 0;
        rate = 0;

        byte b1 = audio[pos + 1];
        byte b2 = audio[pos + 2];
        if (audio[pos] != 0xFF || (b1 & 0xE0) != 0xE0)
            return false;

        int version = (b1 >> 3) & 0x03; // 0 = 2.5, 1 = reserved, 2 = MPEG-2, 3 = MPEG-1
        int layer = (b1 >> 1) & 0x03; // 1 = Layer III
        if (version == 1 || layer != 1)
            return false;

        int bitrateIndex = b2 >> 4;
        int rateIndex = (b2 >> 2) & 0x03;
        bool isMpeg1 = version == 3;
        int kbps = (isMpeg1 ? Mpeg1Layer3Kbps : Mpeg2Layer3Kbps)[bitrateIndex];
        rate = (
            isMpeg1 ? Mpeg1Rates
            : version == 2 ? Mpeg2Rates
            : Mpeg25Rates
        )[rateIndex];
        if (kbps == 0 || rate == 0)
            return false;

        int padding = (b2 >> 1) & 0x01;
        samples = isMpeg1 ? 1152 : 576;
        frameLength = (isMpeg1 ? 144 : 72) * kbps * 1000 / rate + padding;
        return frameLength > 4;
    }
}
