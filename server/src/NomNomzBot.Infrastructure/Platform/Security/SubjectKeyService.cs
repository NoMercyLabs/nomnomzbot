// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// Per-subject DEK lifecycle over the envelope: a 32-byte DEK is generated, wrapped by the KEK
/// (<see cref="IKeyVault"/>), and registered (<see cref="ISubjectKeyStore"/>); field protect/unprotect
/// unwraps the DEK transiently (zeroed after use) and runs AES-256-GCM (<see cref="IFieldCipher"/>);
/// crypto-shred nulls the wrapped DEK so its ciphertext is unrecoverable. The plaintext DEK exists only
/// inside a single call frame and is wiped in <c>finally</c>.
/// </summary>
public sealed class SubjectKeyService : ISubjectKeyService
{
    private const int DekSizeBytes = 32; // AES-256
    private const string Algorithm = "AES-256-GCM";
    private const string SubjectScope = "subject";
    private const string InitialKeyVersion = "1";

    private readonly IKeyVault _keyVault;
    private readonly IFieldCipher _fieldCipher;
    private readonly ISubjectKeyStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubjectKeyService> _logger;

    public SubjectKeyService(
        IKeyVault keyVault,
        IFieldCipher fieldCipher,
        ISubjectKeyStore store,
        TimeProvider timeProvider,
        ILogger<SubjectKeyService> logger
    )
    {
        _keyVault = keyVault;
        _fieldCipher = fieldCipher;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> GetOrCreateSubjectKeyAsync(
        Guid subjectUserId,
        string subjectIdHash,
        CancellationToken cancellationToken = default
    )
    {
        SubjectKeyRecord? existing = await _store.GetActiveByIdentityAsync(
            SubjectScope,
            broadcasterId: null,
            subjectIdHash,
            cancellationToken
        );
        if (existing is not null)
            return Result.Success(existing.Id);

        // Mint a fresh DEK. A prior destroyed key is intentionally not returned above, so a crypto-shredded
        // subject re-enters with a brand-new key — the shredded history stays dead.
        byte[] dek = RandomNumberGenerator.GetBytes(DekSizeBytes);
        try
        {
            Result<WrappedKey> wrapped = await _keyVault.WrapAsync(dek, cancellationToken);
            if (wrapped.IsFailure)
                return wrapped.WithValue(Guid.Empty);

            SubjectKeyRecord record = new()
            {
                Id = Guid.CreateVersion7(),
                KeyScope = SubjectScope,
                BroadcasterId = null,
                SubjectIdHash = subjectIdHash,
                WrappedKeyMaterial = wrapped.Value.WrappedKeyMaterial,
                KekReference = wrapped.Value.KekReference,
                Provider = wrapped.Value.Provider,
                Algorithm = Algorithm,
                KeyVersion = InitialKeyVersion,
                Status = SubjectKeyStatus.Active,
            };

            await _store.AddAsync(record, cancellationToken);
            _logger.LogInformation(
                "Minted subject DEK {KeyId} (provider {Provider}).",
                record.Id,
                record.Provider
            );
            return Result.Success(record.Id);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<Result<CipherPayload>> ProtectAsync(
        Guid cryptoKeyId,
        string plaintext,
        CipherAad aad,
        CancellationToken cancellationToken = default
    )
    {
        Result<(SubjectKeyRecord Record, byte[] Dek)> unwrap = await LoadActiveAndUnwrapAsync(
            cryptoKeyId,
            cancellationToken
        );

        // The active DEK no longer unwraps under the CURRENT KEK (the root key was rotated/replaced, so its
        // wrapped material fails authentication). A protect OVERWRITES the subject's secret, so self-heal: mint a
        // fresh DEK under the current KEK, replace this key's material in place (same key id ⇒ the envelope the
        // caller writes stays consistent), and seal the new value with it. Anything sealed under the lost DEK was
        // already unrecoverable and is being replaced. Gated to UNWRAP_FAILED only — a crypto-shredded key fails
        // with KEY_DESTROYED and is never re-keyed here, so the GDPR erasure guarantee is untouched.
        if (unwrap.IsFailure && unwrap.ErrorCode == "UNWRAP_FAILED")
        {
            Result<byte[]> rekeyed = await RekeyInPlaceAsync(cryptoKeyId, cancellationToken);
            if (rekeyed.IsFailure)
                return rekeyed.ToTyped<CipherPayload>();

            byte[] freshDek = rekeyed.Value;
            try
            {
                return _fieldCipher.Encrypt(freshDek, plaintext, aad);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(freshDek);
            }
        }

        if (unwrap.IsFailure)
            return unwrap.ToTyped<CipherPayload>();

        byte[] dek = unwrap.Value.Dek;
        try
        {
            return _fieldCipher.Encrypt(dek, plaintext, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>
    /// Replaces an active subject key's wrapped material with a freshly-minted DEK sealed under the CURRENT KEK,
    /// keeping the same key id and version. Called only when the existing wrapped DEK fails to unwrap (a rotated
    /// KEK) on a protect (overwrite) — never for a crypto-shredded key. Returns the plaintext fresh DEK for the
    /// caller to seal with; the caller owns zeroing it.
    /// </summary>
    private async Task<Result<byte[]>> RekeyInPlaceAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken
    )
    {
        SubjectKeyRecord? record = await _store.GetAsync(cryptoKeyId, cancellationToken);
        if (record is null)
            return Result.Failure<byte[]>("No such key.", "KEY_NOT_FOUND");
        if (record.Status != SubjectKeyStatus.Active)
            return Result.Failure<byte[]>("The key is not active.", "KEY_NOT_ACTIVE");

        byte[] dek = RandomNumberGenerator.GetBytes(DekSizeBytes);
        bool handedOff = false;
        try
        {
            Result<WrappedKey> wrapped = await _keyVault.WrapAsync(dek, cancellationToken);
            if (wrapped.IsFailure)
                return wrapped.WithValue(Array.Empty<byte>());

            SubjectKeyRecord rekeyed = record with
            {
                WrappedKeyMaterial = wrapped.Value.WrappedKeyMaterial,
                KekReference = wrapped.Value.KekReference,
                Provider = wrapped.Value.Provider,
            };
            await _store.UpdateAsync(rekeyed, cancellationToken);
            _logger.LogWarning(
                "Re-keyed subject DEK {KeyId} in place: the prior DEK was unreadable under the current KEK "
                    + "(root key rotated). Secrets stored under the old key are unrecoverable and must be re-entered.",
                cryptoKeyId
            );
            handedOff = true;
            return Result.Success(dek);
        }
        finally
        {
            if (!handedOff)
                CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<Result<string>> UnprotectAsync(
        Guid cryptoKeyId,
        CipherPayload payload,
        CipherAad aad,
        CancellationToken cancellationToken = default
    )
    {
        Result<(SubjectKeyRecord Record, byte[] Dek)> unwrap = await LoadActiveAndUnwrapAsync(
            cryptoKeyId,
            cancellationToken
        );
        if (unwrap.IsFailure)
            return unwrap.ToTyped<string>();

        byte[] dek = unwrap.Value.Dek;
        try
        {
            return _fieldCipher.Decrypt(dek, payload, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<Result> DestroyKeyAsync(
        Guid cryptoKeyId,
        Guid erasureRequestId,
        CancellationToken cancellationToken = default
    )
    {
        SubjectKeyRecord? record = await _store.GetAsync(cryptoKeyId, cancellationToken);
        if (record is null)
            return Result.Failure("No such key.", "KEY_NOT_FOUND");

        if (record.Status == SubjectKeyStatus.Destroyed)
            return Result.Success(); // idempotent

        SubjectKeyRecord shredded = record with
        {
            Status = SubjectKeyStatus.Destroyed,
            WrappedKeyMaterial = null, // the wrapped DEK is gone → ciphertext is unrecoverable
            KekReference = null,
            DestroyedAt = _timeProvider.GetUtcNow().UtcDateTime,
            ErasureRequestId = erasureRequestId,
        };

        await _store.UpdateAsync(shredded, cancellationToken);
        _logger.LogInformation(
            "Crypto-shredded DEK {KeyId} for erasure request {ErasureRequestId}.",
            cryptoKeyId,
            erasureRequestId
        );
        return Result.Success();
    }

    /// <summary>
    /// Loads the registry record, enforces active status (failing closed with <c>KEY_DESTROYED</c> /
    /// <c>KEY_NOT_ACTIVE</c>), and unwraps the DEK. Caller owns zeroing the returned DEK buffer.
    /// </summary>
    private async Task<Result<(SubjectKeyRecord Record, byte[] Dek)>> LoadActiveAndUnwrapAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken
    )
    {
        SubjectKeyRecord? record = await _store.GetAsync(cryptoKeyId, cancellationToken);
        if (record is null)
            return Result.Failure<(SubjectKeyRecord, byte[])>("No such key.", "KEY_NOT_FOUND");

        if (record.Status == SubjectKeyStatus.Destroyed || record.WrappedKeyMaterial is null)
            return Result.Failure<(SubjectKeyRecord, byte[])>(
                "The key was crypto-shredded; its data is permanently unrecoverable.",
                "KEY_DESTROYED"
            );

        if (record.Status != SubjectKeyStatus.Active)
            return Result.Failure<(SubjectKeyRecord, byte[])>(
                "The key is not active.",
                "KEY_NOT_ACTIVE"
            );

        Result<byte[]> dek = await _keyVault.UnwrapAsync(
            record.WrappedKeyMaterial,
            record.KekReference,
            cancellationToken
        );
        if (dek.IsFailure)
            return dek.WithValue<(SubjectKeyRecord, byte[])>((record, []));

        return Result.Success((record, dek.Value));
    }
}
