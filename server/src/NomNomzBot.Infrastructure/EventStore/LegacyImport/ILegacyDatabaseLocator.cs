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
