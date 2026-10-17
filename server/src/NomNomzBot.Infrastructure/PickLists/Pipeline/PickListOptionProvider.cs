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
using NomNomzBot.Domain.PickLists.Entities;

namespace NomNomzBot.Infrastructure.PickLists.Pipeline;

/// <summary>
/// Supplies the channel's saved pick lists for the <c>pick_list</c> resource-picker kind. The option value is
/// the list NAME, because that is what <c>pick_from_list</c> stores and reads (<c>action.GetString("list")</c>),
/// so pipelines saved before the picker existed keep resolving. <see cref="PipelineOption.SecondaryText"/> is
/// the entry count.
/// </summary>
internal sealed class PickListOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public PickListOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.PickList;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<PickList> query = _db.PickLists.Where(p =>
            p.BroadcasterId == broadcasterId && p.DeletedAt == null
        );
        if (request.IsValueLookup)
        {
            string name = request.Value!;
            query = query.Where(p => p.Name == name);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<PickList> page = await query
            .OrderBy(p => p.Name)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(PickList list)
    {
        string count = list.Items.Count == 1 ? "1 entry" : $"{list.Items.Count} entries";
        return new PipelineOption(
            list.Name,
            list.Name,
            count,
            ImageUrl: null,
            PipelineOptionState.Selectable,
            null
        );
    }
}
