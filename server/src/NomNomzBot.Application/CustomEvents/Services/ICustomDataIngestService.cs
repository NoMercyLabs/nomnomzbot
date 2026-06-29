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
