// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
