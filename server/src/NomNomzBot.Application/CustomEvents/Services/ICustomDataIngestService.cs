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

namespace NomNomzBot.Application.CustomEvents.Services;

/// <summary>
/// The single ingest path for all ingress kinds (push/poll/socket). Extracts fields from the raw
/// payload per the source's <c>FieldMapJson</c>, publishes <c>CustomDataReceivedEvent</c> via
/// <c>IEventBus</c>, and updates the latest-value cache (D4).
/// </summary>
public interface ICustomDataIngestService
{
    Task<Result> IngestAsync(
        Guid broadcasterId,
        string sourceName,
        string rawPayload,
        CancellationToken ct = default
    );
}
