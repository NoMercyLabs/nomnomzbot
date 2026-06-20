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
/// Derives the journal <c>EventId</c> deterministically from a Twitch EventSub message-id (twitch-eventsub
/// §9 decision 1): a name-based UUIDv5 (SHA-1), namespace-scoped to <c>eventsub</c>. The same wire message
/// always maps to the same <see cref="Guid"/>, so the journal's <c>Unique(EventId)</c> and the
/// <c>IdempotencyKey(Scope="eventsub", Key=message-id)</c> agree and replays are exact.
/// </summary>
public static class EventSubMessageId
{
    // A fixed, project-owned namespace UUID for the "eventsub" message-id space. Any stable v4 value works;
    // this one is constant so the derivation is reproducible across processes and deployments.
    private static readonly Guid EventSubNamespace = Guid.Parse(
        "8b1f0d3e-2a6c-4f1b-9c2d-6e5a4b3c2d1e"
    );

    /// <summary>The deterministic v5 GUID for a Twitch message-id. Idempotent: same input ⇒ same output.</summary>
    public static Guid ForMessageId(string messageId) => ToVersion5(EventSubNamespace, messageId);

    private static Guid ToVersion5(Guid namespaceId, string name)
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
