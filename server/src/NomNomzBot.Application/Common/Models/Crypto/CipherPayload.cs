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
/// A sealed AEAD blob. Both fields are base64. <see cref="CipherText"/> is the
/// AES-256-GCM ciphertext with the 16-byte authentication tag appended; <see cref="Nonce"/>
/// is the per-call 96-bit nonce. Maps to the persisted <c>*.CipherText</c> / <c>*.Nonce</c>
/// columns (e.g. <c>IntegrationTokens.CipherText</c> / <c>IntegrationTokens.Nonce</c>).
/// </summary>
public sealed record CipherPayload(string CipherText, string Nonce);
