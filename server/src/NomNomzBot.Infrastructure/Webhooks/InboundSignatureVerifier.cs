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
using NomNomzBot.Application.Contracts.Webhooks;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// The in-box HMAC-SHA256 inbound signature primitive (webhooks.md §3.4). Constant-time comparison; the
/// timestamped overload adds a replay window against the injected clock.
/// </summary>
public sealed class InboundSignatureVerifier(TimeProvider clock) : IInboundSignatureVerifier
{
    public bool Verify(
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> signingString,
        string expectedSignatureHeader,
        string prefix
    )
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(secret, signingString, hash);
        string computed = prefix + Convert.ToHexStringLower(hash);
        return FixedTimeStringEquals(expectedSignatureHeader, computed);
    }

    public bool VerifyWithTimestamp(
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> signingString,
        string expectedSignatureHeader,
        string prefix,
        long timestampUnixSeconds,
        TimeSpan tolerance
    )
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (Math.Abs(now - timestampUnixSeconds) > (long)tolerance.TotalSeconds)
            return false; // outside the replay window
        return Verify(secret, signingString, expectedSignatureHeader, prefix);
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length
            && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
