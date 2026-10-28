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
