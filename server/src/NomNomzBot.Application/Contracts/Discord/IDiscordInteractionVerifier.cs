// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// Ed25519 verification for the Discord interactions webhook. Discord signs every delivery with the
/// application's key pair and sends the signature in <c>X-Signature-Ed25519</c> plus the signed timestamp in
/// <c>X-Signature-Timestamp</c>; the signed message is <c>timestamp + raw request body bytes</c> — verification
/// over re-serialized JSON fails, so the caller must hand over the exact bytes it read from the wire. Any
/// verification failure means the request is not from Discord and must be rejected with 401 before parsing
/// (Discord actively probes endpoints with invalid signatures).
/// </summary>
public interface IDiscordInteractionVerifier
{
    /// <summary>True when a valid <c>Discord:PublicKey</c> (32-byte hex) is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Verifies the hex signature over <c>timestamp + body</c> against the configured application public key.
    /// Missing/malformed headers, an unconfigured key, or a bad signature all return <c>false</c> — never throws.
    /// </summary>
    bool Verify(string? signatureHex, string? timestamp, ReadOnlySpan<byte> body);
}
