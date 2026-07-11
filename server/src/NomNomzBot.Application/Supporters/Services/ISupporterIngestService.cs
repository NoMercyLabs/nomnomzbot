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

namespace NomNomzBot.Application.Supporters.Services;

/// <summary>
/// The single ingest path for supporter events (supporter-events.md §3): resolve the adapter, normalize, dedup
/// on <c>(BroadcasterId, SourceKey, ProviderTransactionId)</c>, persist, and publish one
/// <c>SupporterEventReceived</c>. Called by the webhook bridge, socket, and poll ingress paths.
/// </summary>
public interface ISupporterIngestService
{
    /// <summary>
    /// Ingests one raw provider payload for a broadcaster's source. A disabled/absent connection or a duplicate
    /// transaction is a no-op success (nothing persisted, nothing published). An unknown source or an
    /// unparseable payload is a failure.
    /// </summary>
    Task<Result> IngestAsync(
        Guid broadcasterId,
        string sourceKey,
        string rawPayload,
        CancellationToken ct = default
    );
}
