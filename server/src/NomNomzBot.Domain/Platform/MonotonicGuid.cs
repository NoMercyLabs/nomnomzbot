// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// A version 7 Guid whose order always follows creation order inside this process. <c>Guid.CreateVersion7</c>
/// orders only to the millisecond and fills the rest with random bits, so two Ids made in the same millisecond
/// sort at random. This generator puts a counter in the 12 bits that follow the timestamp (RFC 9562, method 1),
/// so a later Id always sorts higher, in the Guid order, the text order (SQLite) and the byte order (Postgres).
/// </summary>
public static class MonotonicGuid
{
    private const long MaxCounter = 0xFFF;

    private static readonly Lock Gate = new();
    private static long _lastMilliseconds;
    private static long _counter;

    public static Guid Create()
    {
        long milliseconds;
        long counter;
        lock (Gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now > _lastMilliseconds)
            {
                _lastMilliseconds = now;
                _counter = 0;
            }
            else if (_counter < MaxCounter)
            {
                _counter++;
            }
            else
            {
                // 4096 Ids inside one millisecond, or the clock stepped back: borrow the next millisecond.
                _lastMilliseconds++;
                _counter = 0;
            }

            milliseconds = _lastMilliseconds;
            counter = _counter;
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        for (int i = 0; i < 6; i++)
            bytes[i] = (byte)(milliseconds >> (8 * (5 - i)));
        bytes[6] = (byte)(0x70 | (counter >> 8));
        bytes[7] = (byte)(counter & 0xFF);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new(bytes, bigEndian: true);
    }
}
