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
