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

namespace NomNomzBot.Application.Common.Models.Crypto;

/// <summary>
/// Associated data bound into every AES-256-GCM seal as <c>tenantId‖provider‖tokenType‖keyVersion</c>.
/// GCM authenticates (but does not encrypt) the AAD, so a ciphertext sealed under one context can never
/// be decrypted under another: moving a blob to a different tenant / provider / token-type / key-version
/// changes the AAD and the tag verification fails. This is the anti-transplant binding — a ciphertext is
/// non-transplantable across rows or subjects.
/// </summary>
public sealed record CipherAad(
    string TenantId,
    string Provider,
    string TokenType,
    string KeyVersion
)
{
    private const char FieldSeparator = '‖'; // ‖ DOUBLE VERTICAL LINE — not valid in any field value.

    /// <summary>
    /// Canonical UTF-8 byte encoding fed to <c>AesGcm.Encrypt/Decrypt(... associatedData)</c>.
    /// Deterministic and unambiguous: the separator cannot appear inside a field, so distinct
    /// tuples can never collide onto the same byte sequence.
    /// </summary>
    public byte[] ToBytes() =>
        Encoding.UTF8.GetBytes(
            string.Concat(
                TenantId,
                FieldSeparator,
                Provider,
                FieldSeparator,
                TokenType,
                FieldSeparator,
                KeyVersion
            )
        );
}
