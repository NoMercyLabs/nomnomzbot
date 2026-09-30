// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Sound.Audio;

/// <summary>
/// Content-sniff audio files from their magic bytes and probe duration from binary headers.
/// Zero external dependencies — covers mp3 / ogg / wav which are the only accepted formats (spec D4).
/// Duration accuracy: ±1s is acceptable for the <c>WaitForFinish</c> gate in the pipeline action.
/// </summary>
internal static class AudioSniffer
{
    // Known magic bytes for the three accepted formats.
    private static readonly byte[] OggMagic = [0x4F, 0x67, 0x67, 0x53]; // OggS
    private static readonly byte[] WavMagic = [0x52, 0x49, 0x46, 0x46]; // RIFF
    private static readonly byte[] FlacMagic = [0x66, 0x4C, 0x61, 0x43]; // fLaC (rejected)

    public static string? Sniff(byte[] header)
    {
        if (header.Length < 4)
            return null;

        if (IsMatch(header, OggMagic))
            return "audio/ogg";

        if (IsMatch(header, WavMagic))
            return
                header.Length >= 12
                && header[8] == 0x57
                && header[9] == 0x41
                && header[10] == 0x56
                && header[11] == 0x45
                ? "audio/wav"
                : null;

        // MP3: sync word 0xFF 0xE0–0xFF (MPEG-1 layer 3 / 2 / 1 or MPEG-2).
        // Also accept ID3 header: 'I' 'D' '3'.
        if (
            (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            || (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
        )
            return "audio/mpeg";

        return null;
    }

    /// <summary>
    /// Probes a rough duration in milliseconds. The stream must be positioned at offset 0.
    /// Returns 0 if the duration cannot be determined (not an error — the action will use 0ms cap).
    /// </summary>
    public static int ProbeDurationMs(System.IO.Stream stream, string mimeType)
    {
        return mimeType switch
        {
            "audio/wav" or "audio/wave" or "audio/x-wav" => ProbeWavDurationMs(stream),
            "audio/mpeg" => ProbeMp3DurationMs(stream),
            "audio/ogg" => ProbeOggDurationMs(stream),
            _ => 0,
        };
    }

    /// <summary>
    /// Ogg length = the last page's granule position (samples) over the stream's sample rate. The rate comes
    /// from the first packet: a Vorbis identification header carries it; Opus always counts granules at
    /// 48 kHz and starts after its pre-skip. Any other codec returns 0 (unknown).
    /// </summary>
    private static int ProbeOggDurationMs(System.IO.Stream stream)
    {
        try
        {
            if (stream.Length < 64)
                return 0;

            byte[] first = new byte[(int)Math.Min(stream.Length, 512)];
            stream.Position = 0;
            stream.ReadExactly(first, 0, first.Length);
            if (!IsMatch(first, OggMagic))
                return 0;

            int packetStart = 27 + first[26];
            if (packetStart + 19 > first.Length)
                return 0;

            long sampleRate;
            long preSkip = 0;
            if (first[packetStart] == 0x01 && HasAscii(first, packetStart + 1, "vorbis"))
                sampleRate = BitConverter.ToUInt32(first, packetStart + 12);
            else if (HasAscii(first, packetStart, "OpusHead"))
            {
                sampleRate = 48_000;
                preSkip = BitConverter.ToUInt16(first, packetStart + 10);
            }
            else
                return 0;

            if (sampleRate <= 0)
                return 0;

            long granule = LastOggGranule(stream);
            return granule <= preSkip ? 0 : (int)((granule - preSkip) * 1000 / sampleRate);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The granule position of the last Ogg page, found by scanning the file tail backwards.</summary>
    private static long LastOggGranule(System.IO.Stream stream)
    {
        int tailLength = (int)Math.Min(stream.Length, 65_536);
        byte[] tail = new byte[tailLength];
        stream.Position = stream.Length - tailLength;
        stream.ReadExactly(tail, 0, tailLength);

        for (int i = tailLength - 14; i >= 0; i--)
        {
            if (
                tail[i] == 0x4F
                && tail[i + 1] == 0x67
                && tail[i + 2] == 0x67
                && tail[i + 3] == 0x53
            )
                return BitConverter.ToInt64(tail, i + 6);
        }

        return 0;
    }

    private static bool HasAscii(byte[] data, int offset, string text)
    {
        if (offset + text.Length > data.Length)
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (data[offset + i] != text[i])
                return false;
        }
        return true;
    }

    private static int ProbeWavDurationMs(System.IO.Stream stream)
    {
        try
        {
            // RIFF/WAVE layout: RIFF(4) + size(4) + WAVE(4) + fmt (4) + fmtSize(4) + audioFormat(2)
            // + numChannels(2) + sampleRate(4) + byteRate(4) + blockAlign(2) + bitsPerSample(2)
            // + data(4) + dataSize(4). Total header = 44 bytes.
            if (stream.Length < 44)
                return 0;

            byte[] buf = new byte[44];
            stream.Position = 0;
            stream.ReadExactly(buf, 0, 44);

            int byteRate = BitConverter.ToInt32(buf, 28); // bytes per second
            // dataSize is at position 40 in the canonical 44-byte layout.
            int dataSize = BitConverter.ToInt32(buf, 40);

            if (byteRate <= 0)
                return 0;

            return (int)((long)dataSize * 1000 / byteRate);
        }
        catch
        {
            return 0;
        }
    }

    private static int ProbeMp3DurationMs(System.IO.Stream stream)
    {
        try
        {
            // Skip ID3v2 tag if present.
            stream.Position = 0;
            byte[] id3Header = new byte[10];
            if (stream.Read(id3Header, 0, 10) < 10)
                return 0;

            int id3Offset = 0;
            if (id3Header[0] == 0x49 && id3Header[1] == 0x44 && id3Header[2] == 0x33)
            {
                // Syncsafe integer size in bytes 6-9.
                int id3Size =
                    ((id3Header[6] & 0x7F) << 21)
                    | ((id3Header[7] & 0x7F) << 14)
                    | ((id3Header[8] & 0x7F) << 7)
                    | (id3Header[9] & 0x7F);
                id3Offset = 10 + id3Size;
            }

            // Find the first valid MPEG frame header.
            stream.Position = id3Offset;
            byte[] frameBuf = new byte[4];
            for (int i = 0; i < 32768; i++)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    break;
                if (b != 0xFF)
                    continue;
                int b2 = stream.ReadByte();
                if (b2 < 0)
                    break;
                if ((b2 & 0xE0) != 0xE0)
                    continue;

                // Read the remaining 2 bytes of the frame header.
                frameBuf[0] = 0xFF;
                frameBuf[1] = (byte)b2;
                if (stream.Read(frameBuf, 2, 2) < 2)
                    break;

                int bitrate = GetMp3Bitrate(frameBuf);
                int sampleRate = GetMp3SampleRate(frameBuf);

                if (bitrate <= 0 || sampleRate <= 0)
                    continue;

                long fileSize = stream.Length - id3Offset;
                int durationMs = (int)(fileSize * 8L / bitrate); // bitrate in bps
                return durationMs;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    // Returns bitrate in bits-per-second (0 = invalid).
    private static int GetMp3Bitrate(byte[] hdr)
    {
        // MPEG-1 Layer 3 bitrate table (index 0 = free, 15 = bad).
        int[] bitrateTable =
        [
            0,
            32000,
            40000,
            48000,
            56000,
            64000,
            80000,
            96000,
            112000,
            128000,
            160000,
            192000,
            224000,
            256000,
            320000,
            0,
        ];
        int bitrateIndex = (hdr[2] >> 4) & 0x0F;
        return bitrateTable[bitrateIndex];
    }

    // Returns sample rate in Hz (0 = invalid).
    private static int GetMp3SampleRate(byte[] hdr)
    {
        int[] srTable = [44100, 48000, 32000, 0];
        int srIndex = (hdr[2] >> 2) & 0x03;
        return srTable[srIndex];
    }

    private static bool IsMatch(byte[] data, byte[] magic)
    {
        if (data.Length < magic.Length)
            return false;
        for (int i = 0; i < magic.Length; i++)
        {
            if (data[i] != magic[i])
                return false;
        }
        return true;
    }
}
