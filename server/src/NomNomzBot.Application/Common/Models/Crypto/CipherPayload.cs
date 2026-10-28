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
/// A sealed AEAD blob. Both fields are base64. <see cref="CipherText"/> is the
/// AES-256-GCM ciphertext with the 16-byte authentication tag appended; <see cref="Nonce"/>
/// is the per-call 96-bit nonce. Maps to the persisted <c>*.CipherText</c> / <c>*.Nonce</c>
/// columns (e.g. <c>IntegrationTokens.CipherText</c> / <c>IntegrationTokens.Nonce</c>).
/// </summary>
public sealed record CipherPayload(string CipherText, string Nonce);
