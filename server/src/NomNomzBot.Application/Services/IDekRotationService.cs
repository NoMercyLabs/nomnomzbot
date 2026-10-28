// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
