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
