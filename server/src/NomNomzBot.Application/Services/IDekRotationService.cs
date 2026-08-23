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

namespace NomNomzBot.Application.Services;

/// <summary>
/// One DEK per record's wrapped material under <paramref name="CryptoKeyId"/> could not be unwrapped with
/// either the previous or the current root key — it is orphaned (crypto-shredded in all but name) and is
/// reported rather than silently dropped from the pass.
/// </summary>
public sealed record DekRotationFailure(Guid CryptoKeyId, string Reason);

/// <summary>Outcome of one <see cref="IDekRotationService.RotateAllDeksAsync"/> pass.</summary>
public sealed record DekRotationSummary(
    int RewrappedCount,
    int AlreadyCurrentCount,
    IReadOnlyList<DekRotationFailure> Failures
);

/// <summary>
/// Root-key (KEK) rotation: re-wraps every stored DEK from the previous root key to the current one, so
/// changing <c>Encryption:Key</c> does not orphan every stored secret. Does not touch the DEKs themselves or
/// the ciphertext they seal — only the outer KEK-wrap layer changes, so no leaf ciphertext is re-encrypted.
/// Idempotent: a DEK already wrapped under the current key is left untouched and counted separately.
/// </summary>
public interface IDekRotationService
{
    /// <summary>
    /// Walks every non-destroyed DEK, re-wraps the ones still sealed under <paramref name="previousRootKey"/>
    /// with <paramref name="currentRootKey"/>, and persists each re-wrap individually and transactionally
    /// (one DEK's failure never aborts the others). Both keys are base64-encoded 32-byte AES keys in the same
    /// form as <c>Encryption:Key</c>.
    /// </summary>
    Task<Result<DekRotationSummary>> RotateAllDeksAsync(
        string previousRootKey,
        string currentRootKey,
        CancellationToken cancellationToken = default
    );
}
