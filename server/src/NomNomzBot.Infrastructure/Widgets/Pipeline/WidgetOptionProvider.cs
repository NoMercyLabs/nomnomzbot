// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Pipeline;

/// <summary>
/// Supplies the channel's overlay widgets for the <c>widget</c> resource-picker kind (S-RICH-PICKERS).
/// <see cref="PipelineOption.SecondaryText"/> is the widget's kind — its authored framework (vue/react/svelte/
/// vanilla) and install source (first_party/verified_gallery/custom), the two facts that identify a widget
/// beyond its name.
/// </summary>
internal sealed class WidgetOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public WidgetOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Widget;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Widget> query = _db.Widgets.Where(w => w.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(w => w.Name.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<Widget> page = await query
            .OrderBy(w => w.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(Widget widget) =>
        new(
            widget.Id.ToString(),
            widget.Name,
            $"{widget.Framework} · {widget.Source}",
            ImageUrl: null,
            widget.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            widget.IsEnabled ? null : "Widget is disabled."
        );
}
