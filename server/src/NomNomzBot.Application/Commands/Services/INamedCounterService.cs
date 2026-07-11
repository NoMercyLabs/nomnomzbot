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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The per-channel named counters behind <c>{count.&lt;name&gt;}</c> and the <c>set_counter</c> /
/// <c>adjust_counter</c> pipeline actions (commands-pipelines.md, schema G.4) — the channel-scoped
/// sibling of the per-viewer store (per-viewer-data.md D1). Keys are lowercase slugs ≤ 50 chars.
/// </summary>
public interface INamedCounterService
{
    /// <summary>Upsert the counter to an absolute value.</summary>
    Task<Result> SetAsync(
        Guid broadcasterId,
        string key,
        long value,
        CancellationToken ct = default
    );

    /// <summary>Atomic upsert-increment: unset starts at <paramref name="delta"/>; returns the new value.</summary>
    Task<Result<long>> AdjustAsync(
        Guid broadcasterId,
        string key,
        long delta,
        CancellationToken ct = default
    );

    /// <summary>
    /// Bulk read for the template layer: the referenced counters in one round-trip. Missing keys are
    /// absent from the result — the resolver renders them as 0.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, long>>> LoadKeysAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    );
}
