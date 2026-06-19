// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;

namespace NomNomzBot.Application.Common.Interfaces.Crypto;

/// <summary>
/// AEAD data-plane primitive (AES-256-GCM, 16-byte tag, 96-bit random nonce per call). Replaces the
/// legacy AES-CBC <c>IEncryptionService</c>, which had no MAC and no AAD (cross-tenant transplant defect).
/// Stateless: the unwrapped 32-byte DEK is supplied per call and never held. No DB access.
/// </summary>
public interface IFieldCipher
{
    /// <summary>
    /// Seals <paramref name="plaintext"/> under <paramref name="dataEncryptionKey"/>. The
    /// <paramref name="aad"/> is authenticated into the tag so the blob is bound to its row context
    /// (tenant / provider / token-type / key-version) and cannot be transplanted elsewhere. Returns
    /// base64 ciphertext + base64 nonce to persist.
    /// </summary>
    Result<CipherPayload> Encrypt(
        ReadOnlySpan<byte> dataEncryptionKey,
        string plaintext,
        CipherAad aad
    );

    /// <summary>
    /// Opens a <see cref="CipherPayload"/> under <paramref name="dataEncryptionKey"/>. Fails closed
    /// (<c>Result.Failure</c> with code <c>DECRYPT_FAILED</c>) on tag mismatch, AAD mismatch, or any
    /// tampering — it never throws to the caller and never returns a silent or partial plaintext.
    /// </summary>
    Result<string> Decrypt(
        ReadOnlySpan<byte> dataEncryptionKey,
        CipherPayload payload,
        CipherAad aad
    );
}
