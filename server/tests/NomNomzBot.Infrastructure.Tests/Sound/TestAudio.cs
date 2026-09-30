// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>Real, minimal audio files for the sound clip file rules — built byte by byte, no fixtures on disk.</summary>
internal static class TestAudio
{
    private const int WavByteRate = 8_000; // 8 kHz, 8-bit, mono

    /// <summary>A valid PCM WAV of <paramref name="seconds"/> of silence; <paramref name="fill"/> varies the bytes.</summary>
    public static byte[] Wav(int seconds, byte fill = 0x80)
    {
        int dataSize = WavByteRate * seconds;
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataSize);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); // fmt chunk size
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(WavByteRate); // sample rate
        w.Write(WavByteRate); // byte rate
        w.Write((short)1); // block align
        w.Write((short)8); // bits per sample
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataSize);
        w.Write(Enumerable.Repeat(fill, dataSize).ToArray());
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// An Ogg Vorbis stream of two pages: the identification header (44.1 kHz) and a final page whose granule
    /// position says <paramref name="seconds"/> of audio were decoded.
    /// </summary>
    public static byte[] OggVorbis(int seconds)
    {
        const int sampleRate = 44_100;
        byte[] idPacket = new byte[30];
        idPacket[0] = 0x01;
        Encoding.ASCII.GetBytes("vorbis").CopyTo(idPacket, 1);
        idPacket[11] = 1; // channels
        BitConverter.GetBytes(sampleRate).CopyTo(idPacket, 12);

        using MemoryStream ms = new();
        WriteOggPage(ms, headerType: 0x02, granule: 0, sequence: 0, idPacket);
        WriteOggPage(ms, headerType: 0x04, (long)sampleRate * seconds, sequence: 1, [0x00]);
        return ms.ToArray();
    }

    /// <summary>Bytes that start like no supported audio format.</summary>
    public static byte[] NotAudio() => Encoding.ASCII.GetBytes("%PDF-1.7 this is not audio at all");

    private static void WriteOggPage(
        MemoryStream ms,
        byte headerType,
        long granule,
        int sequence,
        byte[] packet
    )
    {
        using BinaryWriter w = new(ms, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("OggS"));
        w.Write((byte)0); // version
        w.Write(headerType);
        w.Write(granule);
        w.Write(0x1234); // serial
        w.Write(sequence);
        w.Write(0); // CRC (not checked by the probe)
        w.Write((byte)1); // one segment
        w.Write((byte)packet.Length);
        w.Write(packet);
    }
}
