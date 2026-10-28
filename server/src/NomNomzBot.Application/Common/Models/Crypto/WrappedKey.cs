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

namespace NomNomzBot.Application.Common.Models.Crypto;

/// <summary>
/// A data-encryption key (DEK) wrapped under the deployment's key-encryption key (KEK).
/// <see cref="WrappedKeyMaterial"/> (base64) and <see cref="KekReference"/> are persisted on the
/// DEK registry row; the plaintext DEK is never stored. <see cref="Provider"/> records which
/// <c>IKeyVault</c> produced the wrap (<c>local_aes</c> | <c>kms_envelope</c>).
/// </summary>
public sealed record WrappedKey(string WrappedKeyMaterial, string? KekReference, string Provider);
