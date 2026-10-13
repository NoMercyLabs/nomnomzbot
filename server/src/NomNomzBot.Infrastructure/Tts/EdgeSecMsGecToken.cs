// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The v2 Sec-MS-GEC token Microsoft's read-aloud handshake demands since 2026-10-13:
/// "&lt;CS64 checksum, 16 hex&gt;-&lt;seeded SHA-256, 64 hex&gt;" over the 5-minute-floored Windows-epoch tick
/// count followed by the trusted client token. A port of rany2/edge-tts 7.3.1 drm.generate_sec_ms_gec_v2
/// (commit ba61d944); the constants are Microsoft's and are kept exactly as the reference has them.
/// </summary>
internal static class EdgeSecMsGecToken
{
    private const long WindowsEpochOffsetSeconds = 11_644_473_600L;

    private static readonly uint[] Sha256Initial =
    [
        0x6A09E667,
        0xBB67AE85,
        0x3C6EF372,
        0xA54FF53A,
        0x510E527F,
        0x9B05688C,
        0x1F83D9AB,
        0x5BE0CD19,
    ];

    // The working state after round 13 of the first block, whose first 7 message bytes it already holds.
    private static readonly uint[] Sha256State =
    [
        0xFD9FBEA2,
        0x91243CE7,
        0xBE499094,
        0xCD2E1E0A,
        0x6C006F49,
        0x04BD35AB,
        0xFEB0A7E4,
        0x03EFFBD2,
    ];

    private static readonly uint[] Sha256Schedule =
    [
        0xEB24BF9E,
        0x1558691F,
        0xA0C09EE2,
        0x0CFA32BA,
        0xA622EE8C,
        0xD1DD9491,
        0xC0DD25A0,
        0xF9E70AB6,
        0xF0C31573,
        0x7058CE4E,
        0x7188E606,
        0x00E5AB33,
        0xD258B060,
        0x72766564,
    ];

    private static readonly uint[] RoundConstants =
    [
        0x428A2F98,
        0x71374491,
        0xB5C0FBCF,
        0xE9B5DBA5,
        0x3956C25B,
        0x59F111F1,
        0x923F82A4,
        0xAB1C5ED5,
        0xD807AA98,
        0x12835B01,
        0x243185BE,
        0x550C7DC3,
        0x72BE5D74,
        0x80DEB1FE,
        0x9BDC06A7,
        0xC19BF174,
        0xE49B69C1,
        0xEFBE4786,
        0x0FC19DC6,
        0x240CA1CC,
        0x2DE92C6F,
        0x4A7484AA,
        0x5CB0A9DC,
        0x76F988DA,
        0x983E5152,
        0xA831C66D,
        0xB00327C8,
        0xBF597FC7,
        0xC6E00BF3,
        0xD5A79147,
        0x06CA6351,
        0x14292967,
        0x27B70A85,
        0x2E1B2138,
        0x4D2C6DFC,
        0x53380D13,
        0x650A7354,
        0x766A0ABB,
        0x81C2C92E,
        0x92722C85,
        0xA2BFE8A1,
        0xA81A664B,
        0xC24B8B70,
        0xC76C51A3,
        0xD192E819,
        0xD6990624,
        0xF40E3585,
        0x106AA070,
        0x19A4C116,
        0x1E376C08,
        0x2748774C,
        0x34B0BCB5,
        0x391C0CB3,
        0x4ED8AA4A,
        0x5B9CCA4F,
        0x682E6FF3,
        0x748F82EE,
        0x78A5636F,
        0x84C87814,
        0x8CC70208,
        0x90BEFFFA,
        0xA4506CEB,
        0xBEF9A3F7,
        0xC67178F2,
    ];

    private const uint Sha256Adjustment = 0xC9DC06A7;
    private const uint Sigma0Adjustment = 0x059C0B80;
    private const uint Sigma1Adjustment = 0x000B92C0;

    private static readonly uint[] FirstBlockConstants = BuildFirstBlockConstants();

    private static readonly (uint Coefficient, uint Offset)[] Cs64Coefficients =
    [
        (0xE82E91B1, 0xB32700D0),
        (0x44DE24C6, 0x88DFF51D),
        (0x3BEB65BC, 0x29514038),
        (0xC89C5933, 0xB328CBDF),
        (0x8D1EC211, 0xD405B4D5),
        (0x314EF5D5, 0x26E55F3C),
        (0xE92491B1, 0x607801C6),
        (0xF0A5B9D5, 0x82B2C1FB),
        (0x06064CF3, 0xFA51A3DA),
        (0xDA79FA60, 0x207CC464),
        (0xBED20560, 0x3C3C74F3),
        (0x89F9C9F8, 0xD2D14B61),
        (0x35FA38C6, 0xBB04A2C1),
        (0xCB49E3C6, 0xC5626167),
    ];

    private const uint Cs64Factor = 0x82893933;
    private const uint Cs64Adjustment0 = 0xF2F90B1A;
    private const uint Cs64Adjustment1 = 0x0B1A0000;

    public static string Generate(DateTimeOffset now, string trustedClientToken)
    {
        long windowsEpochSeconds = now.ToUnixTimeSeconds() + WindowsEpochOffsetSeconds;
        long flooredToFiveMinutes = windowsEpochSeconds - (windowsEpochSeconds % 300);
        long hundredNanosecondTicks = flooredToFiveMinutes * 10_000_000L;
        byte[] data = Encoding.ASCII.GetBytes($"{hundredNanosecondTicks}{trustedClientToken}");

        byte[] digest = Sha256Digest(data);
        (uint first, uint second) = Cs64Digest(data, digest);

        byte[] cs64 = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(cs64, first);
        BinaryPrimitives.WriteUInt32LittleEndian(cs64.AsSpan(4), second);

        return $"{Convert.ToHexString(cs64)}-{Convert.ToHexString(digest)}";
    }

    private static uint[] BuildFirstBlockConstants()
    {
        uint[] constants = (uint[])RoundConstants.Clone();
        constants[14] = Sha256Adjustment;
        return constants;
    }

    private static uint Rol16(uint value) => (value << 16) | (value >> 16);

    private static uint Rotr(uint value, int bits) => (value >> bits) | (value << (32 - bits));

    private static uint SmallSigma0(uint value) => Rotr(value, 7) ^ Rotr(value, 18) ^ (value >> 3);

    private static uint SmallSigma1(uint value) =>
        Rotr(value, 17) ^ Rotr(value, 19) ^ (value >> 10);

    private static void ExpandSchedule(uint[] words, int firstWord)
    {
        for (int index = firstWord; index < 64; index++)
        {
            words[index] = unchecked(
                SmallSigma1(words[index - 2])
                + words[index - 7]
                + SmallSigma0(words[index - 15])
                + words[index - 16]
            );
        }
    }

    /// <summary>Plain SHA-256 rounds from <paramref name="firstRound"/> to 63 on a copy of the state.</summary>
    private static uint[] Rounds(uint[] state, uint[] words, int firstRound, uint[] constants)
    {
        uint a = state[0],
            b = state[1],
            c = state[2],
            d = state[3];
        uint e = state[4],
            f = state[5],
            g = state[6],
            h = state[7];

        for (int index = firstRound; index < 64; index++)
        {
            uint bigSigma1 = Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25);
            uint choose = g ^ (e & (f ^ g));
            uint t1 = unchecked(h + bigSigma1 + choose + constants[index] + words[index]);
            uint bigSigma0 = Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22);
            uint majority = (a & b) | (c & (a | b));
            uint t2 = unchecked(bigSigma0 + majority);

            h = g;
            g = f;
            f = e;
            e = unchecked(d + t1);
            d = c;
            c = b;
            b = a;
            a = unchecked(t1 + t2);
        }

        return [a, b, c, d, e, f, g, h];
    }

    private static uint[] AddStates(uint[] left, uint[] right)
    {
        uint[] sum = new uint[8];
        for (int index = 0; index < 8; index++)
            sum[index] = unchecked(left[index] + right[index]);
        return sum;
    }

    /// <summary>The state after the first 7 message bytes, from Microsoft's seeded schedule.</summary>
    private static uint[] InitialState(ReadOnlySpan<byte> firstSeven)
    {
        uint[] words = new uint[64];
        words[14] = ((uint)firstSeven[0] << 16) | ((uint)firstSeven[1] << 8) | firstSeven[2];
        words[15] = BinaryPrimitives.ReadUInt32BigEndian(firstSeven[3..7]);

        for (int index = 16; index < 30; index++)
            words[index] = SeededScheduleWord(words, index);

        words[30] = unchecked(
            SmallSigma1(words[28])
            + words[23]
            + SmallSigma0(words[15])
            + words[14]
            + Sha256Adjustment
            - RoundConstants[14]
        );
        ExpandSchedule(words, 31);

        uint[] working = Rounds(Sha256State, words, 14, FirstBlockConstants);
        return AddStates(Sha256Initial, working);
    }

    private static uint SeededScheduleWord(uint[] words, int index)
    {
        uint value = Sha256Schedule[index - 16];

        if (index - 2 >= 14)
        {
            uint sigma1 = SmallSigma1(words[index - 2]);
            value = unchecked(value + (index == 16 ? sigma1 ^ Sigma1Adjustment : sigma1));
        }

        if (index - 7 >= 14)
            value = unchecked(value + words[index - 7]);

        if (index - 15 >= 14)
        {
            uint sigma0 = SmallSigma0(words[index - 15]);
            value = unchecked(value + (index == 29 ? sigma0 ^ Sigma0Adjustment : sigma0));
        }

        return value;
    }

    private static byte[] Sha256Digest(byte[] data)
    {
        if (data.Length < 7)
            throw new ArgumentException("Token data must hold at least seven bytes.", nameof(data));

        int length = 57 + data.Length;
        int zeros = (((55 - length) % 64) + 64) % 64;
        byte[] padded = new byte[data.Length - 7 + 1 + zeros + 8];
        data.AsSpan(7).CopyTo(padded);
        padded[data.Length - 7] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length - 8), (ulong)length * 8);

        uint[] state = InitialState(data.AsSpan(0, 7));
        for (int offset = 0; offset < padded.Length; offset += 64)
        {
            uint[] words = new uint[64];
            for (int index = 0; index < 16; index++)
                words[index] = BinaryPrimitives.ReadUInt32BigEndian(
                    padded.AsSpan(offset + (index * 4), 4)
                );
            ExpandSchedule(words, 16);

            uint[] working = Rounds(state, words, 0, RoundConstants);
            state = AddStates(state, working);
        }

        byte[] digest = new byte[32];
        for (int index = 0; index < 8; index++)
            BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(index * 4), state[index]);
        return digest;
    }

    /// <summary>Little-endian 32-bit words, zero-padded to a whole word and then to an even count.</summary>
    private static uint[] LittleEndianWordsEven(byte[] data)
    {
        int wordCount = (data.Length + 3) / 4;
        int evenCount = wordCount + (wordCount % 2);
        byte[] padded = new byte[evenCount * 4];
        data.CopyTo(padded, 0);

        uint[] words = new uint[evenCount];
        for (int index = 0; index < evenCount; index++)
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(index * 4, 4));
        return words;
    }

    private static (uint First, uint Second) Cs64Digest(byte[] data, byte[] digest)
    {
        uint keyA = BinaryPrimitives.ReadUInt32LittleEndian(digest) | 1;
        uint keyB = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4)) | 1;
        uint[] factors = [unchecked(keyA * Cs64Factor), unchecked(keyB * Cs64Factor)];

        List<uint> terms = [];
        List<uint> offsets = [];
        for (int index = 0; index < Cs64Coefficients.Length; index++)
        {
            terms.Add(unchecked(Cs64Coefficients[index].Coefficient * factors[index % 2]));
            offsets.Add(Cs64Coefficients[index].Offset);
        }

        byte[] prefixed = new byte[data.Length + 1];
        data.CopyTo(prefixed, 1);
        uint[] words = LittleEndianWordsEven(prefixed);
        for (int index = 0; index < words.Length; index++)
        {
            bool even = index % 2 == 0;
            uint term = unchecked(words[index] * (even ? keyA : keyB));
            uint offset = unchecked(Rol16(words[index]) * (even ? 0xEF0569FBu : 0xC31713DBu));
            if (index == 0)
            {
                term = unchecked(term + (Cs64Adjustment0 * factors[0]));
                offset = unchecked(offset + Cs64Adjustment1);
            }
            terms.Add(term);
            offsets.Add(offset);
        }

        return Fold(terms, offsets, keyA, keyB);
    }

    private static (uint First, uint Second) Fold(
        List<uint> terms,
        List<uint> offsets,
        uint keyA,
        uint keyB
    )
    {
        uint swapAcc = 0,
            swapLast = 0,
            state = 0,
            acc = 0;
        unchecked
        {
            for (int index = 0; index < terms.Count; index += 2)
            {
                uint x = terms[index];
                uint y = terms[index + 1];

                uint q = Rol16(offsets[index] + x) * 0x76F50F34;
                swapAcc += (Rol16(q) * 0xC3EFEA97) + q;
                q = Rol16(offsets[index + 1] + y) * 0x1881CE3C;
                swapLast = (Rol16(q) * 0x35BD1EC9) + q;
                swapAcc += swapLast;

                state = (state * keyA) + x;
                state = Rol16(state) * 0xCF98B111;
                state = Rol16(state) * 0x87085B9F;
                state = Rol16(state) * 0x12CEB96D;
                state = Rol16(state) * 0x257E1D83;
                acc += state;
                uint t = (state * keyB) + y;
                t = Rol16(t) * 0xA27416F5;
                t = Rol16(t) * 0xD38396FF;
                state = Rol16(Rol16(t) * 0x7C932B89) * 0xBFA49F69;
                acc += state;
            }
        }

        return (swapLast ^ state, swapAcc ^ acc);
    }
}
