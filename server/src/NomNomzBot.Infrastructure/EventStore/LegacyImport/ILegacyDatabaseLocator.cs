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
/// Resolves the on-disk path of the legacy NoMercy bot's SQLite database to import from. A seam so the import service
/// can be tested without the real filesystem, and so an operator can point the import at a moved/backup file.
/// </summary>
public interface ILegacyDatabaseLocator
{
    /// <summary>
    /// Returns the legacy database file path if it exists, or a failure (<c>LEGACY_DB_NOT_FOUND</c>) describing where
    /// it was looked for.
    /// </summary>
    Result<string> Resolve();
}
