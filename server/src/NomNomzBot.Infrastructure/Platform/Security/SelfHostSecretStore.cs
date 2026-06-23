// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// First-run self-host custody for platform secrets the operator should never have to set — today the JWT
/// signing secret. On first boot a strong value is generated once and persisted OS-natively (Windows DPAPI,
/// machine scope; a user-only file elsewhere), beside the root KEK under the machine app-data dir. Subsequent
/// boots reload the same value, so the single executable runs on a clean launch and issued tokens survive
/// restarts. SaaS deployments supply their own secret and never reach this.
/// </summary>
public static class SelfHostSecretStore
{
    // Mixed into DPAPI so an unrelated process can't unprotect the blob with the machine key alone; distinct
    // from the KEK store's entropy so the two sealed values are not interchangeable.
    private static readonly byte[] Entropy = "nomnomzbot-jwt-secret-v1"u8.ToArray();

    /// <summary>
    /// Returns the persisted JWT signing secret, generating + sealing a fresh 64-char base64 value (384 bits)
    /// on first run. <paramref name="keysDirectory"/> defaults to the machine app-data keys dir (overridable
    /// for tests).
    /// </summary>
    public static string LoadOrCreateJwtSecret(string? keysDirectory = null)
    {
        string path = Path.Combine(keysDirectory ?? DefaultKeysDirectory(), "jwt-secret.bin");

        if (File.Exists(path))
            return Decode(File.ReadAllBytes(path));

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteSealed(path, secret);
        return secret;
    }

    private static void WriteSealed(string path, string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(
                path,
                ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine)
            );
            return;
        }

        // Non-Windows: no DPAPI — store plaintext in a file the OS restricts to the owning user only.
        File.WriteAllBytes(path, plaintext);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Decode(byte[] sealedBytes) =>
        OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(
                ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.LocalMachine)
            )
            : Encoding.UTF8.GetString(sealedBytes);

    private static string DefaultKeysDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolderOption.Create
            ),
            "NomNomzBot",
            "keys"
        );
}
