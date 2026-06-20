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

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// A read-model that folds journal events into derived state. Implemented by each read-model (event log,
/// redemptions, leaderboards, watch sessions/streaks, the economy balance projection, …). NOTE: the economy
/// ledger (K.3) is an independent source of truth, NOT a projection — only its derived balance projects here.
/// </summary>
public interface IProjection
{
    /// <summary>Stable unique name; matches <c>ProjectionCheckpoint.ProjectionName</c>. Constant per impl.</summary>
    string Name { get; }

    /// <summary>
    /// True if this projection consumes the global cross-tenant stream (a <c>BroadcasterId == null</c>
    /// checkpoint), false if it runs once per tenant. Drives which checkpoint row(s) the runner manages.
    /// </summary>
    bool IsGlobal { get; }

    /// <summary>The EventTypes this projection cares about; the runner skips others. Empty = all types.</summary>
    IReadOnlySet<string> SubscribedEventTypes { get; }

    /// <summary>
    /// Applies one event to the read model. MUST be idempotent (safe to re-apply during replay): upsert keyed
    /// on <c>EventId</c>/natural key, never a blind insert. Mutates only the read-model table.
    /// </summary>
    Task<Result> ApplyAsync(EventRecord @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the read model to empty for the given scope (<c>null</c> = all tenants) before a rebuild-from-zero.
    /// Deletes/truncates this projection's derived rows for the scope.
    /// </summary>
    Task<Result> ResetAsync(Guid? broadcasterId, CancellationToken cancellationToken = default);
}
