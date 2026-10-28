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

namespace NomNomzBot.Application.Common.Models;

/// <summary>
/// Normalised paging request shared by every list service (<c>.Skip((Page-1)*PageSize).Take(PageSize)</c>).
/// The page index and size are clamped on construction so a client cannot request an unbounded page
/// (e.g. <c>pageSize=1000000</c>) and force the server to materialise a huge result set into memory.
/// </summary>
public record PaginationParams(
    int Page = 1,
    int PageSize = 25,
    string? SortBy = null,
    string? Order = "asc"
)
{
    /// <summary>Hard ceiling on a single page; larger requests are clamped down to this.</summary>
    public const int MaxPageSize = 100;

    public int Page { get; init; } = Page < 1 ? 1 : Page;
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, MaxPageSize);
}
