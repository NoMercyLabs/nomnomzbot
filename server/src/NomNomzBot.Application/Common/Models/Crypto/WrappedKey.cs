// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models.Crypto;

/// <summary>
/// A data-encryption key (DEK) wrapped under the deployment's key-encryption key (KEK).
/// <see cref="WrappedKeyMaterial"/> (base64) and <see cref="KekReference"/> are persisted on the
/// DEK registry row; the plaintext DEK is never stored. <see cref="Provider"/> records which
/// <c>IKeyVault</c> produced the wrap (<c>local_aes</c> | <c>kms_envelope</c>).
/// </summary>
public sealed record WrappedKey(string WrappedKeyMaterial, string? KekReference, string Provider);
