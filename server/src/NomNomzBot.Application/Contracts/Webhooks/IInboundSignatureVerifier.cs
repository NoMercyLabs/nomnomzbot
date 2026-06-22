// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>
/// The in-box HMAC-SHA256 primitive the inbound adapters share (webhooks.md §3.4). Constant-time comparison; the
/// signing string is provider-specific, so the adapter builds it and this verifies. No third-party crypto.
/// </summary>
public interface IInboundSignatureVerifier
{
    /// <summary>
    /// True iff <paramref name="expectedSignatureHeader"/> equals <paramref name="prefix"/> +
    /// lowerhex(HMAC-SHA256(secret, signingString)), compared in constant time. <paramref name="prefix"/> is the
    /// provider token (e.g. <c>"sha256="</c>); empty for raw-hex schemes.
    /// </summary>
    bool Verify(
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> signingString,
        string expectedSignatureHeader,
        string prefix
    );

    /// <summary>As <see cref="Verify"/>, but also rejects when the timestamp is older than <paramref name="tolerance"/> (replay guard).</summary>
    bool VerifyWithTimestamp(
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> signingString,
        string expectedSignatureHeader,
        string prefix,
        long timestampUnixSeconds,
        TimeSpan tolerance
    );
}
