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
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<ChannelAsset> query = _db.ChannelAssets.Where(a =>
            a.BroadcasterId == broadcasterId
        );
        if (request.IsValueLookup)
        {
            Guid valueId = request.ValueAsGuid ?? Guid.Empty;
            query = query.Where(a => a.Id == valueId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(a =>
                a.DisplayName.ToLower().Contains(term) || a.Name.ToLower().Contains(term)
            );
        }

        int total = await query.CountAsync(ct);
        List<ChannelAsset> page = await query
            .OrderBy(a => a.DisplayName)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
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
