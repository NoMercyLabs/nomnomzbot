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
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Infrastructure.Sound.Pipeline;

/// <summary>
/// Supplies the channel's sound-clip library for the <c>sound_clip</c> resource-picker kind (S-RICH-PICKERS).
/// <see cref="PipelineOption.SecondaryText"/> is the clip's duration — the field that actually tells two clips
/// apart in a list of names.
/// </summary>
internal sealed class SoundClipOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public SoundClipOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.SoundClip;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<SoundClip> query = _db.SoundClips.Where(c => c.BroadcasterId == broadcasterId);
        if (request.IsValueLookup)
        {
            Guid valueId = request.ValueAsGuid ?? Guid.Empty;
            query = query.Where(c => c.Id == valueId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(c =>
                c.DisplayName.ToLower().Contains(term) || c.Name.ToLower().Contains(term)
            );
        }

        int total = await query.CountAsync(ct);
        List<SoundClip> page = await query
            .OrderBy(c => c.DisplayName)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(SoundClip clip)
    {
        double seconds = clip.DurationMs / 1000.0;
        return new PipelineOption(
            clip.Id.ToString(),
            clip.DisplayName,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds:0.0}s"),
            ImageUrl: null,
            clip.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            clip.IsEnabled ? null : "Sound clip is disabled."
        );
    }
}
