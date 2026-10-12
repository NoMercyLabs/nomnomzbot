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
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// Self-host KEK-custody adapter (<c>Provider = local_aes</c>). The root KEK is a 32-byte AES key whose
/// custody is the OS-native secure store: on Windows the KEK is generated once and persisted DPAPI-protected
/// (<see cref="ProtectedData"/>, machine-bound) so it never sits in plaintext on disk; macOS Keychain and
/// Linux libsecret are the equivalent backends on those platforms (P/Invoke, follow-up). When a deployment
/// key is configured (<c>Encryption:Key</c>) — the CI / dev / headless / non-Windows path — the KEK is
/// derived deterministically from it via HKDF so it is stable across restarts without any OS keystore.
///
/// The KEK wraps DEKs with AES-256-GCM under a fixed wrap-context AAD: this is the outer envelope layer.
/// Only the root KEK is OS-custodied; the wrapped DEKs and all ciphertext live in the DB, so OS custody
/// (root) and the AES-256-GCM-under-DEK data plane compose into envelope encryption.
/// </summary>
public sealed class OsSecureStoreKeyVault : IKeyVault
{
    private const int KekSizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    // Wrap-context AAD binds wrapped DEKs to the wrapping purpose so a wrapped DEK cannot be replayed as a
    // field ciphertext (different AAD namespace), and a future KEK-version tag can extend it.
    private static readonly byte[] WrapContext = Encoding.UTF8.GetBytes("nomnomzbot:dek-wrap:v1");

    private const string OsKekReference = "os-secure-store:dpapi";
    private const string ConfigKekReference = "config:hkdf";

    // New DEKs wrap under the active KEK; an existing DEK unwraps under the KEK its persisted reference names,
    // so setting Encryption:Key on an install that started on the OS store keeps the older secrets readable.
    private readonly Lazy<byte[]>? _configKek;
    private readonly Lazy<byte[]> _osKek;
    private readonly string _kekReference;

    public string Provider => "local_aes";

    public OsSecureStoreKeyVault(
        IOptions<EncryptionOptions> options,
        ILogger<OsSecureStoreKeyVault> logger
    )
        : this(options.Value, () => LoadOsKek(logger), logger) { }

    internal OsSecureStoreKeyVault(EncryptionOptions opts, Func<byte[]> loadOsKek, ILogger logger)
    {
        _configKek = string.IsNullOrWhiteSpace(opts.Key)
            ? null
            : new(
                () => DeriveConfigKek(opts.Key, logger),
                LazyThreadSafetyMode.ExecutionAndPublication
            );
        _osKek = new(loadOsKek, LazyThreadSafetyMode.ExecutionAndPublication);
        _kekReference = _configKek is null ? OsKekReference : ConfigKekReference;
    }

    private byte[] ActiveKek => (_configKek ?? _osKek).Value;

    public Task<Result<WrappedKey>> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken = default
    )
    {
        if (dataEncryptionKey.Length != KekSizeBytes)
            return Task.FromResult(
                Result.Failure<WrappedKey>("DEK must be exactly 32 bytes.", "INVALID_KEY")
            );

        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] cipher = new byte[dataEncryptionKey.Length];
        byte[] tag = new byte[TagSizeBytes];

        using AesGcm aes = new(ActiveKek, TagSizeBytes);
        aes.Encrypt(nonce, dataEncryptionKey.Span, cipher, tag, WrapContext);

        // Persisted wrapped form: nonce‖cipher‖tag, base64.
        byte[] wrapped = new byte[nonce.Length + cipher.Length + tag.Length];
        nonce.CopyTo(wrapped, 0);
        cipher.CopyTo(wrapped, nonce.Length);
        tag.CopyTo(wrapped, nonce.Length + cipher.Length);

        return Task.FromResult(
            Result.Success(new WrappedKey(Convert.ToBase64String(wrapped), _kekReference, Provider))
        );
    }

    public Task<Result<byte[]>> UnwrapAsync(
        string wrappedKeyMaterial,
        string? kekReference,
        CancellationToken cancellationToken = default
    )
    {
        byte[] wrapped;
        try
        {
            wrapped = Convert.FromBase64String(wrappedKeyMaterial);
        }
        catch (FormatException)
        {
            return Task.FromResult(
                Result.Failure<byte[]>("Wrapped key material is not valid base64.", "UNWRAP_FAILED")
            );
        }

        if (wrapped.Length != NonceSizeBytes + KekSizeBytes + TagSizeBytes)
            return Task.FromResult(
                Result.Failure<byte[]>("Wrapped key material length is invalid.", "UNWRAP_FAILED")
            );

        ReadOnlySpan<byte> nonce = wrapped.AsSpan(0, NonceSizeBytes);
        ReadOnlySpan<byte> cipher = wrapped.AsSpan(NonceSizeBytes, KekSizeBytes);
        ReadOnlySpan<byte> tag = wrapped.AsSpan(NonceSizeBytes + KekSizeBytes, TagSizeBytes);
        byte[] dek = new byte[KekSizeBytes];

        Result<byte[]> kek = KekFor(kekReference);
        if (kek.IsFailure)
            return Task.FromResult(kek);

        try
        {
            using AesGcm aes = new(kek.Value, TagSizeBytes);
            aes.Decrypt(nonce, cipher, tag, dek, WrapContext);
            return Task.FromResult(Result.Success(dek));
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(dek);
            return Task.FromResult(
                Result.Failure<byte[]>(
                    "KEK unwrap authentication failed — wrong KEK or corrupted material.",
                    "UNWRAP_FAILED"
                )
            );
        }
    }

    private Result<byte[]> KekFor(string? kekReference)
    {
        switch (kekReference)
        {
            case ConfigKekReference when _configKek is null:
                return Result.Failure<byte[]>(
                    "The DEK was wrapped under Encryption:Key, which is no longer configured.",
                    "UNWRAP_FAILED"
                );
            case ConfigKekReference:
                return Result.Success(_configKek.Value);
            case OsKekReference:
                try
                {
                    return Result.Success(_osKek.Value);
                }
                catch (Exception ex)
                    when (ex is InvalidOperationException or CryptographicException)
                {
                    return Result.Failure<byte[]>(
                        $"The DEK was wrapped under the OS secure store, which cannot be read here: {ex.Message}",
                        "UNWRAP_FAILED"
                    );
                }
            default:
                return Result.Success(ActiveKek);
        }
    }

    // ─── KEK acquisition ────────────────────────────────────────────────────────

    // Config / env KEK path (CI, dev, headless, non-Windows): deterministic across restarts via HKDF,
    // so wrapped DEKs unwrap consistently. This is the deterministic fallback custody.
    private static byte[] DeriveConfigKek(string key, ILogger logger)
    {
        byte[] ikm = Convert.FromBase64String(key);
        try
        {
            byte[] derived = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm,
                KekSizeBytes,
                salt: null,
                info: WrapContext
            );
            logger.LogInformation("KEK derived from configured deployment key (HKDF-SHA256).");
            return derived;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }

    // OS-native custody path: DPAPI on Windows. A random KEK is generated once and persisted
    // DPAPI-protected; subsequent boots unprotect it. macOS Keychain / Linux libsecret
    // are the equivalent backends and are the platform follow-ups.
    private static byte[] LoadOsKek(ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            byte[] kek = DpapiKekStore.LoadOrCreate(logger);
            logger.LogInformation("KEK loaded from OS-native secure store (Windows DPAPI).");
            return kek;
        }

        throw new InvalidOperationException(
            "No KEK custody available: configure Encryption:Key for the deterministic fallback, or run on a "
                + "platform with an implemented OS-native secure store (Windows DPAPI). macOS Keychain / Linux "
                + "libsecret backends are pending."
        );
    }
}
