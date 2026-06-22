// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Startup gate that refuses to boot outside development when the JWT signing key or the encryption
/// fallback key is still a bundled, publicly-known default (or the JWT key is too short). A committed
/// default JWT key lets anyone forge tokens for any user; a default <c>Encryption:Key</c> makes the
/// deterministic-fallback KEK public. Pure + static so the boot-blocking behavior is unit-tested directly.
/// </summary>
public static class StartupSecretGuard
{
    // The development defaults shipped in appsettings.json and the Program.cs fallback.
    private const string DevEncryptionKey = "ZGV2LWVuY3J5cHRpb24ta2V5LWZvci1sb2NhbC1kZXY=";

    private static readonly string[] DevJwtSecrets =
    [
        "change-me-in-production-at-least-32-chars!",
        "dev-secret-key-at-least-32-characters-long!!",
    ];

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when, outside development, the JWT secret is a known
    /// default or shorter than 32 chars, or the encryption key is the bundled development key. No-op in
    /// development, where the defaults are expected.
    /// </summary>
    public static void Validate(string jwtSecret, string? encryptionKey, bool isDevelopment)
    {
        if (isDevelopment)
            return;

        if (jwtSecret.Length < 32 || DevJwtSecrets.Contains(jwtSecret))
            throw new InvalidOperationException(
                "Jwt:Secret must be a strong, non-default value (>= 32 chars) in production. "
                    + "Generate one with: openssl rand -base64 32"
            );

        if (encryptionKey == DevEncryptionKey)
            throw new InvalidOperationException(
                "Encryption:Key is still the bundled development key. Set a strong random value "
                    + "(openssl rand -base64 32), or rely on OS-native KEK custody (DPAPI / Keychain)."
            );
    }
}
