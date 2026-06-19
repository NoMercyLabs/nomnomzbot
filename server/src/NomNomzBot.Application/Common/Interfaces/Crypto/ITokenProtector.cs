// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Interfaces.Crypto;

/// <summary>
/// Application-facing facade for protecting stored OAuth secrets (Twitch / Spotify access &amp; refresh
/// tokens, client secrets). Replaces the legacy AES-CBC <c>IEncryptionService</c>. Each call declares its
/// <see cref="TokenProtectionContext"/> (the subject the secret belongs to + which field it is), so the
/// facade resolves a per-subject DEK via the envelope (<c>ISubjectKeyService</c>) and binds the AAD to that
/// subject + field — making each ciphertext non-transplantable. The whole sealed envelope (key id, nonce,
/// ciphertext) is serialized into the single existing token column, so no schema change is required.
/// </summary>
public interface ITokenProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the subject's DEK (minted on first use) and returns the
    /// self-describing sealed envelope string to persist in the token column.
    /// </summary>
    Task<string> ProtectAsync(
        string plaintext,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Opens a sealed envelope produced by <see cref="ProtectAsync"/>. Returns null when the value is
    /// absent, malformed, the subject DEK was crypto-shredded, or authentication fails — never throws and
    /// never returns a partial plaintext. Mirrors the legacy <c>TryDecrypt</c> contract for call sites.
    /// </summary>
    Task<string?> TryUnprotectAsync(
        string? sealedEnvelope,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Identifies whose secret this is and which field, so the DEK is per-subject and the AAD is per-field.
/// <paramref name="SubjectId"/> is the owning identity (broadcaster id, or a stable sentinel for the shared
/// bot account); <paramref name="Provider"/> is the integration (<c>twitch</c> / <c>spotify</c> …);
/// <paramref name="Field"/> is the column role (<c>access</c> / <c>refresh</c> / <c>client_secret</c> …).
/// </summary>
public sealed record TokenProtectionContext(string SubjectId, string Provider, string Field);
