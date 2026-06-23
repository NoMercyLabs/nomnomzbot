// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Persists the port a self-host install has committed to (deployment-distribution §6). Read on every boot and written
/// once, on the first boot, so the bot keeps the same port — and therefore the same OAuth redirect URLs — across
/// restarts. Abstracted so <see cref="ListenPortBootstrap"/> is unit-testable without touching the real data dir.
/// </summary>
public interface ILockedPortStore
{
    /// <summary>The committed port, or <c>null</c> if this install has never locked one (first boot).</summary>
    int? Read();

    /// <summary>Commit (lock) the port so later boots reuse it.</summary>
    void Write(int port);
}

/// <summary>
/// File-backed <see cref="ILockedPortStore"/>: a single line holding the port number, under the self-host data dir
/// (<see cref="SelfHostDataPaths.ListenPortFile"/>). Best-effort — a read or write failure leaves the install unlocked
/// (it simply re-resolves its port next boot) rather than crashing.
/// </summary>
public sealed class FileLockedPortStore : ILockedPortStore
{
    private readonly string _path;

    public FileLockedPortStore()
        : this(SelfHostDataPaths.ListenPortFile) { }

    public FileLockedPortStore(string path) => _path = path;

    public int? Read()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            string text = File.ReadAllText(_path).Trim();
            return
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                && port is > 0 and <= 65535
                ? port
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(int port)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, port.ToString(CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // Best-effort: a failed lock-write just means the bot re-resolves its port on the next boot.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — never let an unwritable data dir crash boot.
        }
    }
}
