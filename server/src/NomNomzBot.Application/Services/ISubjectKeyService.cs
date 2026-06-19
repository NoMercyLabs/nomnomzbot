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

namespace NomNomzBot.Application.Services;

/// <summary>
/// Per-subject / per-tenant DEK lifecycle. The only component that materializes a plaintext DEK — and only
/// transiently, zeroed immediately after use. Composes <c>IKeyVault</c> (KEK wrap/unwrap),
/// <c>IFieldCipher</c> (AES-256-GCM), and <c>ISubjectKeyStore</c> (the DEK registry). Envelope encryption:
/// a freshly generated DEK seals the field, the KEK wraps the DEK, and crypto-shred deletes the wrapped
/// DEK so every ciphertext under it becomes permanently unrecoverable.
/// </summary>
public interface ISubjectKeyService
{
    /// <summary>
    /// Returns the active DEK id for a subject, minting one if absent (generate 32-byte DEK, wrap via
    /// vault, persist an <c>active</c> record). Idempotent per <c>(subject, subjectIdHash)</c>: a present
    /// active key is returned as-is. A prior <c>destroyed</c> key is never resurrected — a fresh DEK is
    /// minted, so a crypto-shredded subject re-enters as a clean slate.
    /// </summary>
    Task<Result<Guid>> GetOrCreateSubjectKeyAsync(
        Guid subjectUserId,
        string subjectIdHash,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Seals <paramref name="plaintext"/> under the named DEK: loads the record, unwraps the DEK via the
    /// vault, AES-256-GCM-encrypts with the supplied <paramref name="aad"/>, and returns the
    /// <see cref="CipherPayload"/> to persist. Fails closed (<c>KEY_DESTROYED</c>) when the key was
    /// crypto-shredded, or (<c>KEY_NOT_ACTIVE</c>) for any non-active status.
    /// </summary>
    Task<Result<CipherPayload>> ProtectAsync(
        Guid cryptoKeyId,
        string plaintext,
        CipherAad aad,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Opens a <see cref="CipherPayload"/> under the named DEK. Returns failure <c>KEY_DESTROYED</c> when
    /// the DEK was crypto-shredded (the GDPR guarantee surfaced to callers, not an exception) and
    /// <c>DECRYPT_FAILED</c> on tag / AAD mismatch or tampering.
    /// </summary>
    Task<Result<string>> UnprotectAsync(
        Guid cryptoKeyId,
        CipherPayload payload,
        CipherAad aad,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// CRYPTO-SHRED (O(1)). Flips <c>Status</c> to <c>destroyed</c>, nulls the wrapped DEK material, and
    /// stamps <c>DestroyedAt</c> + the erasure request. Every ciphertext sealed under this DEK becomes
    /// permanently unreadable (backups included), because the DEK can no longer be unwrapped. Idempotent
    /// on an already-destroyed key.
    /// </summary>
    Task<Result> DestroyKeyAsync(
        Guid cryptoKeyId,
        Guid erasureRequestId,
        CancellationToken cancellationToken = default
    );
}
