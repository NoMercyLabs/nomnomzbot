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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Per-channel key/value storage for sandboxed scripts (custom-code.md §6.2 <c>storage.*</c>) — the durable
/// state a script keeps between runs (counters, holder-of-the-feather, todo lists). Backed by the tenant rows
/// of the <c>Storage</c> table, namespaced under a script-only key prefix so script keys can never collide
/// with (or enumerate) any other storage use. Bounded and fail-closed: keys are capped in length, values in
/// size, and each channel in key count — an over-cap write is a typed failure, never a partial write.
/// </summary>
public interface IScriptStorageService
{
    /// <summary>Longest user-visible key accepted (characters).</summary>
    const int MaxKeyLength = 128;

    /// <summary>Largest value accepted, in UTF-8 bytes (64 KB).</summary>
    const int MaxValueBytes = 64 * 1024;

    /// <summary>Most script keys one channel may hold.</summary>
    const int MaxKeysPerChannel = 200;

    /// <summary>The stored value for <paramref name="key"/> in this channel, or null when absent/invalid.</summary>
    Task<string?> GetAsync(Guid broadcasterId, string key, CancellationToken ct = default);

    /// <summary>
    /// Upserts <paramref name="key"/> = <paramref name="value"/> for this channel. Fails (typed, nothing
    /// written) on an invalid key, a value over <see cref="MaxValueBytes"/>, or a NEW key beyond
    /// <see cref="MaxKeysPerChannel"/> (updating an existing key never counts against the cap).
    /// </summary>
    Task<Result> SetAsync(
        Guid broadcasterId,
        string key,
        string value,
        CancellationToken ct = default
    );

    /// <summary>Removes <paramref name="key"/> from this channel; idempotent (a missing key still succeeds).</summary>
    Task<Result> DeleteAsync(Guid broadcasterId, string key, CancellationToken ct = default);

    /// <summary>
    /// The channel's script keys (user-visible names, sorted ordinally), optionally filtered to those starting
    /// with <paramref name="prefix"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(
        Guid broadcasterId,
        string? prefix = null,
        CancellationToken ct = default
    );
}
