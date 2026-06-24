// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Resolves the legacy NoMercy bot database from its well-known per-user location — <c>%LOCALAPPDATA%\NoMercyBot\data\database.sqlite</c>
/// — a sibling of this bot's own <c>NomNomzBot</c> data directory, since both bots store their data under the same
/// per-user profile root. An operator can override the full path with the <c>NOMNOMZ_LEGACY_DB</c> environment
/// variable (a moved file, a backup, a container volume). The file is never written by the import.
/// </summary>
public sealed class DefaultLegacyDatabaseLocator : ILegacyDatabaseLocator
{
    public Result<string> Resolve()
    {
        string? overridePath = Environment.GetEnvironmentVariable("NOMNOMZ_LEGACY_DB");
        string path = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoMercyBot",
                "data",
                "database.sqlite"
            );

        return File.Exists(path)
            ? Result.Success(path)
            : Result.Failure<string>(
                $"Legacy database not found at '{path}'. Set NOMNOMZ_LEGACY_DB to its location.",
                "LEGACY_DB_NOT_FOUND"
            );
    }
}
