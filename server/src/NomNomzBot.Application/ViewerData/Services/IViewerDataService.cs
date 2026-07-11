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

namespace NomNomzBot.Application.ViewerData.Services;

/// <summary>
/// The writable per-viewer key/value store (per-viewer-data.md §3) — arbitrary per-viewer custom data a
/// pipeline sets and reads (a per-viewer death counter, "favorite game", a quest flag). Keys are slugs
/// (lowercase <c>[a-z0-9_-]</c>, ≤ 50 chars); values are bounded strings; numeric ops parse as <see cref="long"/>.
/// Writes are tenant + viewer scoped and capped per viewer (D5) — an over-cap write is rejected, never truncated.
/// </summary>
public interface IViewerDataService
{
    Task<Result<string?>> GetAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyDictionary<string, string>>> ListForViewerAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    Task<Result> SetAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        string value,
        CancellationToken ct = default
    );

    /// <summary>Atomic numeric upsert-increment: unset starts at <paramref name="delta"/>; returns the new value.</summary>
    Task<Result<long>> AdjustAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        long delta,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        CancellationToken ct = default
    );

    /// <summary>
    /// Bulk read for the template layer: the referenced keys for one viewer in a single round-trip.
    /// Missing keys are simply absent from the result — the resolver renders them empty.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, string>>> LoadKeysAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    );
}
