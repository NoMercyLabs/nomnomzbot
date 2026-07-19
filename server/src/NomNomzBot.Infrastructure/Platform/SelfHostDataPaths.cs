// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// The single on-disk home for a self-host install's runtime data — the SQLite database, the rolling logs, and
/// the OS-vault key files — so everything lives in one discoverable, backup-able folder instead of scattered
/// relative to whatever directory the executable happened to be launched from. Defaults to the current user's
/// per-platform profile data directory — <c>%LOCALAPPDATA%\NomNomzBot</c> (Windows), <c>~/.local/share/NomNomzBot</c>
/// (Linux, XDG), <c>~/Library/Application Support/NomNomzBot</c> (macOS) — so a self-host install keeps its data
/// with the user that runs it. A container or operator overrides it with the <c>NOMNOMZ_DATA_DIR</c> environment
/// variable (the Docker image points it at a mounted volume).
/// </summary>
public static class SelfHostDataPaths
{
    /// <summary>The resolved base data directory (created on first access).</summary>
    public static string BaseDirectory { get; } = ResolveBaseDirectory();

    public static string DatabaseFile => Path.Combine(BaseDirectory, "nomnomz.db");

    public static string SqliteConnectionString => $"Data Source={DatabaseFile}";

    public static string LogsDirectory => Ensure(Path.Combine(BaseDirectory, "logs"));

    public static string KeysDirectory => Ensure(Path.Combine(BaseDirectory, "keys"));

    /// <summary>Durable storage for broadcaster-uploaded sound clips (spec P.18).</summary>
    public static string SoundClipsDirectory => Ensure(Path.Combine(BaseDirectory, "sound-clips"));

    /// <summary>Durable storage for broadcaster-uploaded media assets (the widget/overlay asset library).</summary>
    public static string AssetsDirectory => Ensure(Path.Combine(BaseDirectory, "assets"));

    /// <summary>
    /// The file that records the TCP port this install has committed to (deployment-distribution §6). Written the
    /// first time the bot binds a self-host loopback port; read on every later boot so the port — and therefore the
    /// OAuth redirect URLs registered against it — stays stable. Delete it to let the bot re-pick its port.
    /// </summary>
    public static string ListenPortFile => Path.Combine(BaseDirectory, "listen-port");

    private static string ResolveBaseDirectory()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("NOMNOMZ_DATA_DIR");
        string baseDir = !string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.Create
                ),
                "NomNomzBot"
            );
        return Ensure(baseDir);
    }

    private static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
