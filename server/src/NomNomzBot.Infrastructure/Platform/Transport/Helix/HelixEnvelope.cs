// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// The generic Helix response envelope (twitch-helix.md §4.2). Every Helix read wraps its rows in a
/// <c>data[]</c> array, paged endpoints add a <c>pagination.cursor</c>, and count endpoints add a top-level
/// <c>total</c>. The transport deserialises into this once (System.Text.Json, snake_case wire) and exposes
/// items + cursor + total generically, so per-endpoint methods only supply the row type <typeparamref name="T"/>.
/// </summary>
internal sealed class HelixEnvelope<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; init; }

    [JsonPropertyName("pagination")]
    public HelixPagination? Pagination { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

/// <summary>The cursor inside a paged Helix response; <c>cursor</c> is absent on the final page.</summary>
internal sealed class HelixPagination
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>The Helix error body (twitch-helix.md §3) — surfaced into <c>Result.ErrorDetail</c> for diagnostics.</summary>
internal sealed class HelixErrorBody
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("status")]
    public int? Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
