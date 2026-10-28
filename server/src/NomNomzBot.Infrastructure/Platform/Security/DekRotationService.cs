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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// KEK-rotation re-wrap pass over the DEK registry (<see cref="ISubjectKeyStore"/>). Builds two throwaway
/// <see cref="OsSecureStoreKeyVault"/> instances — one bound to the previous <c>Encryption:Key</c>, one to
/// the current — and, for each non-destroyed DEK, tries the current key first (already rotated, nothing to
/// do), then the previous key (re-wrap under the current key and persist), and reports anything that
/// unwraps under neither as a named failure rather than aborting the pass.
/// </summary>
public sealed class DekRotationService : IDekRotationService
{
    private readonly ISubjectKeyStore _store;
    private readonly ILogger<DekRotationService> _logger;
    private readonly ILoggerFactory _vaultLoggerFactory;

    public DekRotationService(
        ISubjectKeyStore store,
        ILogger<DekRotationService> logger,
        ILoggerFactory vaultLoggerFactory
    )
    {
        _store = store;
        _logger = logger;
        _vaultLoggerFactory = vaultLoggerFactory;
    }

    public async Task<Result<DekRotationSummary>> RotateAllDeksAsync(
        string previousRootKey,
        string currentRootKey,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(previousRootKey) || string.IsNullOrWhiteSpace(currentRootKey))
            return Result.Failure<DekRotationSummary>(
                "Both the previous and current root key are required.",
                "VALIDATION_FAILED"
            );

        IKeyVault previousVault = BuildVault(previousRootKey);
        IKeyVault currentVault = BuildVault(currentRootKey);

        IReadOnlyList<SubjectKeyRecord> records = await _store.GetAllNonDestroyedAsync(
            cancellationToken
        );

        int rewrapped = 0;
        int alreadyCurrent = 0;
        List<DekRotationFailure> failures = [];

        foreach (SubjectKeyRecord record in records)
        {
            if (record.WrappedKeyMaterial is null)
            {
                // Non-destroyed by status but no material — nothing to re-wrap; not a failure.
                continue;
            }

            Result<byte[]> underCurrent = await currentVault.UnwrapAsync(
                record.WrappedKeyMaterial,
                record.KekReference,
                cancellationToken
            );
            if (underCurrent.IsSuccess)
            {
                CryptographicZero(underCurrent.Value);
                alreadyCurrent++;
                continue;
            }

            Result<byte[]> underPrevious = await previousVault.UnwrapAsync(
                record.WrappedKeyMaterial,
                record.KekReference,
                cancellationToken
            );
            if (underPrevious.IsFailure)
            {
                failures.Add(
                    new(record.Id, "Unwraps under neither the previous nor the current root key.")
                );
                _logger.LogError(
                    "DEK {KeyId} could not be re-wrapped: unwraps under neither root key.",
                    record.Id
                );
                continue;
            }

            byte[] dek = underPrevious.Value;
            try
            {
                Result<WrappedKey> rewrappedKey = await currentVault.WrapAsync(
                    dek,
                    cancellationToken
                );
                if (rewrappedKey.IsFailure)
                {
                    failures.Add(new(record.Id, rewrappedKey.ErrorMessage ?? "Wrap failed."));
                    continue;
                }

                SubjectKeyRecord updated = record with
                {
                    WrappedKeyMaterial = rewrappedKey.Value.WrappedKeyMaterial,
                    KekReference = rewrappedKey.Value.KekReference,
                    Provider = rewrappedKey.Value.Provider,
                };
                await _store.UpdateAsync(updated, cancellationToken);
                rewrapped++;
                _logger.LogInformation(
                    "Re-wrapped DEK {KeyId} under the current root key.",
                    record.Id
                );
            }
            finally
            {
                CryptographicZero(dek);
            }
        }

        return Result.Success(new DekRotationSummary(rewrapped, alreadyCurrent, failures));
    }

    private IKeyVault BuildVault(string rootKeyBase64) =>
        new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = rootKeyBase64 }),
            _vaultLoggerFactory.CreateLogger<OsSecureStoreKeyVault>()
        );

    private static void CryptographicZero(byte[] buffer) =>
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
}
