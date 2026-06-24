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
using System.Text;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// RFC 4122 name-based UUID version 5 (SHA-1) derivation. Same <c>(namespace, name)</c> always yields the same
/// <see cref="Guid"/>, so it is the reproducible idempotency key for deriving a journal <c>EventId</c> from an
/// external, non-GUID identifier (a Twitch EventSub message-id, a legacy bot row id). This is the single
/// implementation of the byte-order-correct v5 algorithm; callers supply their own namespace so different id
/// spaces never collide.
/// </summary>
public static class NameBasedGuid
{
    public static Guid Version5(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] toHash = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, toHash, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, toHash, namespaceBytes.Length, nameBytes.Length);

        byte[] hash = SHA1.HashData(toHash);

        byte[] result = new byte[16];
        Array.Copy(hash, 0, result, 0, 16);

        // Version 5 (name-based, SHA-1): high nibble of byte 6 = 0101.
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        // RFC 4122 variant: top two bits of byte 8 = 10.
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapByteOrder(result);
        return new Guid(result);
    }

    // .NET's Guid byte layout is little-endian for the first three fields; RFC 4122 hashing is big-endian.
    private static void SwapByteOrder(byte[] guid)
    {
        SwapBytes(guid, 0, 3);
        SwapBytes(guid, 1, 2);
        SwapBytes(guid, 4, 5);
        SwapBytes(guid, 6, 7);
    }

    private static void SwapBytes(byte[] guid, int left, int right) =>
        (guid[left], guid[right]) = (guid[right], guid[left]);
}
