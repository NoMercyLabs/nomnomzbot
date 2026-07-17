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
using NomNomzBot.Application.Import.Dtos;

namespace NomNomzBot.Application.Import.Services;

/// <summary>
/// Imports a streamer's configuration from another bot platform into a channel. Each entity is mapped onto the
/// channel's own commands / quotes / timers through the existing management services, so every import passes the
/// same validation, quota, and live-sync path as a hand-created entry. Duplicates are skipped and counted, never
/// fatal, which makes an import idempotent.
/// </summary>
public interface IProviderImportService
{
    /// <summary>
    /// Imports the commands, quotes, and timers from a StreamElements chatbot export into
    /// <paramref name="broadcasterId"/>'s channel. Returns a summary of what landed versus was skipped.
    /// </summary>
    Task<Result<ImportSummary>> ImportStreamElementsAsync(
        Guid broadcasterId,
        StreamElementsExport export,
        CancellationToken ct = default
    );
}
