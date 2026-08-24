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
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<SoundClip> query = _db.SoundClips.Where(c => c.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(c =>
                c.DisplayName.ToLower().Contains(term) || c.Name.ToLower().Contains(term)
            );
        }

        int total = await query.CountAsync(ct);
        List<SoundClip> page = await query
            .OrderBy(c => c.DisplayName)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
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
