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
