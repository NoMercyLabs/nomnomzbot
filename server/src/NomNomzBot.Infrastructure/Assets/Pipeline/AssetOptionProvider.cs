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
using NomNomzBot.Domain.Assets.Entities;

namespace NomNomzBot.Infrastructure.Assets.Pipeline;

/// <summary>
/// Supplies the channel's media asset library for the <c>asset</c> resource-picker kind (S-RICH-PICKERS).
/// <see cref="PipelineOption.SecondaryText"/> is the asset's kind (image/audio) and size — this source can grow
/// large (uploads), so results are paginated and searchable by name.
/// </summary>
internal sealed class AssetOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public AssetOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Asset;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<ChannelAsset> query = _db.ChannelAssets.Where(a =>
            a.BroadcasterId == broadcasterId
        );
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(a =>
                a.DisplayName.ToLower().Contains(term) || a.Name.ToLower().Contains(term)
            );
        }

        int total = await query.CountAsync(ct);
        List<ChannelAsset> page = await query
            .OrderBy(a => a.DisplayName)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(ChannelAsset asset) =>
        new(
            asset.Id.ToString(),
            asset.DisplayName,
            $"{asset.Kind} · {FormatSize(asset.SizeBytes)}",
            ImageUrl: asset.Kind == "image"
                ? $"/api/v1/assets/file/{asset.BroadcasterId}/{asset.Name}"
                : null,
            PipelineOptionState.Selectable
        );

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{bytes / (1024.0 * 1024.0):0.0} MB"
            )
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{bytes / 1024.0:0.0} KB"
            );
}
