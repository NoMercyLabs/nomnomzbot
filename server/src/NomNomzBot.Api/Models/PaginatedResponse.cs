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

namespace NomNomzBot.Api.Models;

public record PaginatedResponse<T>
{
    [JsonPropertyName("data")]
    public IEnumerable<T> Data { get; set; } = [];

    [JsonPropertyName("nextPage")]
    public int? NextPage { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }
}

public class PageRequestDto
{
    public int Page { get; set; } = 1;

    private int _take = 25;

    /// <summary>
    /// Rows per page. Bound from EITHER <c>Take</c> (the historical name) OR <c>pageSize</c> — the REST-
    /// convention name every frontend caller actually sends. Before this alias, <c>?pageSize=100</c> bound to
    /// nothing and silently fell back to 25, so every dashboard list was capped at 25 rows and the rest of the
    /// data was invisible. Both query keys now populate the same value.
    /// </summary>
    public int Take
    {
        get => _take;
        set => _take = value;
    }

    public int PageSize
    {
        get => _take;
        set => _take = value;
    }

    public string? Sort { get; set; }
    public string? Order { get; set; } = "asc";
    public string? Search { get; set; }
}
