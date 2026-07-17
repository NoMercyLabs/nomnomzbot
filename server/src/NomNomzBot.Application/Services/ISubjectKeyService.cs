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
    /// Returns the active <c>tenant</c>-scope DEK for a channel, minting one if absent. Idempotent per
    /// broadcaster: a present active key is returned as-is; a destroyed one is never resurrected.
    /// </summary>
    Task<Result<Guid>> GetOrCreateTenantKeyAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the active <c>platform</c>-scope DEK for a named purpose (e.g. a platform-global sealed
    /// setting), minting one if absent. Idempotent per purpose. The purpose keys the registry identity
    /// (it occupies the <c>SubjectIdHash</c> slot, ≤ 64 chars).
    /// </summary>
    Task<Result<Guid>> GetOrCreatePlatformKeyAsync(
        string purpose,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Seals <paramref name="plaintext"/> under the named DEK: loads the record, unwraps the DEK via the
    /// vault, AES-256-GCM-encrypts with the supplied <paramref name="aad"/>, and returns the
    /// <see cref="CipherPayload"/> to persist. Records/asserts a <c>KeyUsageBinding</c> (Q.2) for
    /// <c>(cryptoKeyId, resourceTable, resourceColumn)</c> so the shred/rotation planners know which
    /// resource holds ciphertext under the DEK. Fails closed (<c>KEY_DESTROYED</c>) when the key was
    /// crypto-shredded, or (<c>KEY_NOT_ACTIVE</c>) for any non-active status — a <c>rotating</c>
    /// predecessor never seals new data.
    /// </summary>
    Task<Result<CipherPayload>> ProtectAsync(
        Guid cryptoKeyId,
        string plaintext,
        CipherAad aad,
        string resourceTable,
        string resourceColumn,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Opens a <see cref="CipherPayload"/> under the named DEK. A <c>rotating</c> predecessor still opens
    /// its old ciphertext (rotation is lazy — see <see cref="RotateKeyAsync"/>). Returns failure
    /// <c>KEY_DESTROYED</c> when the DEK was crypto-shredded (the GDPR guarantee surfaced to callers, not
    /// an exception) and <c>DECRYPT_FAILED</c> on tag / AAD mismatch or tampering.
    /// </summary>
    Task<Result<string>> UnprotectAsync(
        Guid cryptoKeyId,
        CipherPayload payload,
        CipherAad aad,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Rotates a DEK: mints a successor generation (fresh DEK, <c>KeyVersion + 1</c>,
    /// <c>RotatedFromKeyId</c> linking back) and retires the old key to <c>rotating</c> in one atomic
    /// store write — the predecessor's wrapped material is RETAINED (rotation ≠ shred), so data sealed
    /// under it keeps decrypting via <see cref="UnprotectAsync"/> while every new write lands on the
    /// successor (the identity's sole <c>active</c> key). Usage bindings carry over to the successor.
    /// Re-encryption of the inventoried rows is lazy — each resource owner re-seals on its next write, and
    /// <see cref="ResolveSubjectKeysAsync"/> keeps every generation in the shred set until then.
    /// Returns the successor key id. Fails closed on a destroyed (<c>KEY_DESTROYED</c>) or non-active
    /// (<c>KEY_NOT_ACTIVE</c>) key.
    /// </summary>
    Task<Result<Guid>> RotateKeyAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolves every DEK to shred for a subject (the erasure pipeline's read-only planning step): the
    /// <c>Users.SubjectKeyId</c> FK, every <c>subject</c>-scope generation registered under the hash
    /// (rotated predecessors included), and every DEK mapped to the subject via <c>EventSubjectKeys</c>
    /// (multi-subject journal events, O.1a). Already-destroyed keys are excluded. Shared
    /// <c>tenant</c>/<c>platform</c> keys are NEVER returned — they seal other subjects' data, so
    /// destroying them for one subject's erasure would violate everyone else's.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>>> ResolveSubjectKeysAsync(
        Guid subjectUserId,
        string subjectIdHash,
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
