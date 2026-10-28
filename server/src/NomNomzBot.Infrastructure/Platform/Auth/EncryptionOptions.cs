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

namespace NomNomzBot.Infrastructure.Platform.Auth;

public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// Base64-encoded secret key used for AES-256 token encryption.
    /// Must be the same across restarts — store in environment variable ENCRYPTION__KEY.
    /// </summary>
    public string Key { get; set; } = null!;
}
