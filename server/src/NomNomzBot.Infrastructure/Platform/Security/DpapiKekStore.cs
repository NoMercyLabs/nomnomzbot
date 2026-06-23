// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// Windows OS-native KEK custody backend. The root KEK is a 32-byte AES key generated once on first run and
/// persisted DPAPI-protected (machine scope) under the app data dir, so it is bound to the machine and never
/// stored in plaintext. Subsequent boots DPAPI-unprotect the same KEK, keeping wrapped DEKs unwrappable.
/// Windows-only — gated by <see cref="OperatingSystem.IsWindows"/> at the call site.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DpapiKekStore
{
    private const int KekSizeBytes = 32;

    // Entropy mixed into DPAPI so an unrelated process cannot unprotect the blob with the machine key alone.
    private static readonly byte[] Entropy =
    [
        0x6E,
        0x6F,
        0x6D,
        0x6E,
        0x6F,
        0x6D,
        0x7A,
        0x62,
        0x6F,
        0x74,
        0x6B,
        0x65,
        0x6B,
        0x76,
        0x31,
        0x00,
    ];

    public static byte[] LoadOrCreate(ILogger logger)
    {
        string path = KekFilePath();

        if (File.Exists(path))
        {
            byte[] protectedBytes = File.ReadAllBytes(path);
            return ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.LocalMachine
            );
        }

        byte[] kek = RandomNumberGenerator.GetBytes(KekSizeBytes);
        byte[] sealedBytes = ProtectedData.Protect(kek, Entropy, DataProtectionScope.LocalMachine);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, sealedBytes);
        logger.LogInformation("Generated and DPAPI-protected a new root KEK at {Path}.", path);

        return kek;
    }

    private static string KekFilePath() =>
        Path.Combine(SelfHostDataPaths.KeysDirectory, "root-kek.dpapi");
}
