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
using NomNomzBot.Application.Common.Models.Crypto;

namespace NomNomzBot.Application.Common.Interfaces.Crypto;

/// <summary>
/// KEK-custody adapter (the deployment's root key-encryption key). Wraps / unwraps the 32-byte DEK under
/// the active KEK; only the wrapped material and a KEK reference are persisted, never a plaintext DEK.
/// Profile-selected: <c>OsSecureStoreKeyVault</c> (self-host — root KEK in the OS-native secure store,
/// e.g. Windows DPAPI) or <c>AzureKeyVaultKeyVault</c> (SaaS — Managed-HSM). Operates on raw key bytes
/// only; knows nothing of EF / rows. The unwrapped DEK buffer is the caller's to zero after use.
/// </summary>
public interface IKeyVault
{
    /// <summary>Provider tag written to the DEK registry: <c>local_aes</c> | <c>kms_envelope</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// Wraps a freshly generated 32-byte DEK under the active KEK. Returns the wrapped ciphertext plus
    /// the KEK reference to persist (<c>WrappedKeyMaterial</c> / <c>KekReference</c>). No DB write.
    /// </summary>
    Task<Result<WrappedKey>> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Unwraps <paramref name="wrappedKeyMaterial"/> back to the 32-byte DEK for in-process AEAD. The
    /// caller must zero the returned buffer via <c>CryptographicOperations.ZeroMemory</c> after use.
    /// No DB write.
    /// </summary>
    Task<Result<byte[]>> UnwrapAsync(
        string wrappedKeyMaterial,
        string? kekReference,
        CancellationToken cancellationToken = default
    );
}
