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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Pipeline;

/// <summary>
/// Supplies the channel's overlay widgets for the <c>widget</c> resource-picker kind (S-RICH-PICKERS).
/// <see cref="PipelineOption.SecondaryText"/> is the widget's kind — its authored framework (vue/react/
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
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<Widget> query = _db.Widgets.Where(w => w.BroadcasterId == broadcasterId);
        if (request.IsValueLookup)
        {
            Guid valueId = request.ValueAsGuid ?? Guid.Empty;
            query = query.Where(w => w.Id == valueId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(w => w.Name.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<Widget> page = await query
            .OrderBy(w => w.Name)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
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
