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
using Microsoft.Extensions.Logging;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// Fallback KEK custody for hosts with no OS-native secure store (Docker / Linux) and no configured
/// <c>Encryption:Key</c>. The root KEK is 32 random bytes generated once and kept in a file in the data folder
/// (owner read/write only on Unix), so it survives restarts and travels with the data-folder backup.
/// Losing the file loses every secret that was wrapped under it.
/// </summary>
internal static class DataFolderKekStore
{
    internal const string FileName = "root-kek.key";
    private const int KekSizeBytes = 32;

    public static byte[] LoadOrCreate(string directory, ILogger logger)
    {
        string path = Path.Combine(directory, FileName);

        if (File.Exists(path))
            return ReadKek(path);

        Directory.CreateDirectory(directory);
        byte[] kek = RandomNumberGenerator.GetBytes(KekSizeBytes);

        try
        {
            using FileStream stream = new(path, CreateNewOptions());
            stream.Write(kek);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process created it first: use its key so both agree.
            CryptographicOperations.ZeroMemory(kek);
            return ReadKek(path);
        }

        logger.LogInformation(
            "No Encryption:Key is configured and no OS secure store exists: generated a root KEK and saved it at {Path}. "
                + "Back up this file with the data folder; without it the saved secrets cannot be read.",
            path
        );
        return kek;
    }

    private static byte[] ReadKek(string path)
    {
        byte[] kek = File.ReadAllBytes(path);
        if (kek.Length == KekSizeBytes)
            return kek;

        CryptographicOperations.ZeroMemory(kek);
        throw new InvalidOperationException(
            $"The root KEK file at {path} is not {KekSizeBytes} bytes. Restore it from a backup; "
                + "do not delete it, because secrets already saved cannot be read without it."
        );
    }

    private static FileStreamOptions CreateNewOptions()
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }
}
