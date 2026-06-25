// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page * PageSize < TotalCount;

    /// <summary>
    /// Builds a page. Argument order is <c>(items, page, pageSize, totalCount)</c> — <b>totalCount LAST</b>. There
    /// was once a second ctor in a different order (<c>(items, total, page, pageSize)</c>), but a <c>List&lt;T&gt;</c>
    /// always bound to THIS one (its <c>IReadOnlyList</c> param is the better match), so any call written in that
    /// other order silently set <see cref="TotalCount"/> to the page size — a bug that hit 16 list services before
    /// it was found. That trap ctor was removed; this is the only one. Pass a materialised list/array.
    /// </summary>
    public PagedList(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }
}
