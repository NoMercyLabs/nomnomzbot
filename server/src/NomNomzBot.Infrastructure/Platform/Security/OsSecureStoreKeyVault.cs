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

    private readonly Lazy<byte[]> _kek;
    private readonly string _kekReference;

    public string Provider => "local_aes";

    public OsSecureStoreKeyVault(
        IOptions<EncryptionOptions> options,
        ILogger<OsSecureStoreKeyVault> logger
    )
    {
        EncryptionOptions opts = options.Value;
        _kek = new Lazy<byte[]>(
            () => ResolveKek(opts, logger),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _kekReference = string.IsNullOrWhiteSpace(opts.Key)
            ? "os-secure-store:dpapi"
            : "config:hkdf";
    }

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

        using AesGcm aes = new(_kek.Value, TagSizeBytes);
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

        try
        {
            using AesGcm aes = new(_kek.Value, TagSizeBytes);
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

    // ─── KEK acquisition ────────────────────────────────────────────────────────

    private static byte[] ResolveKek(EncryptionOptions opts, ILogger logger)
    {
        // Config / env KEK path (CI, dev, headless, non-Windows): deterministic across restarts via HKDF,
        // so wrapped DEKs unwrap consistently. This is the deterministic fallback custody.
        if (!string.IsNullOrWhiteSpace(opts.Key))
        {
            byte[] ikm = Convert.FromBase64String(opts.Key);
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
        // DPAPI-protected (machine scope); subsequent boots unprotect it. macOS Keychain / Linux libsecret
        // are the equivalent backends and are the platform follow-ups.
        if (OperatingSystem.IsWindows())
        {
            byte[] kek = DpapiKekStore.LoadOrCreate(logger);
            logger.LogInformation(
                "KEK loaded from OS-native secure store (Windows DPAPI, machine scope)."
            );
            return kek;
        }

        throw new InvalidOperationException(
            "No KEK custody available: configure Encryption:Key for the deterministic fallback, or run on a "
                + "platform with an implemented OS-native secure store (Windows DPAPI). macOS Keychain / Linux "
                + "libsecret backends are pending."
        );
    }
}
