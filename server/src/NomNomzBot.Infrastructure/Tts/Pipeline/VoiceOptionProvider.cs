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
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts.Pipeline;

/// <summary>
/// Supplies the seeded/synced TTS voice catalogue for the <c>voice</c> resource-picker kind (S-RICH-PICKERS).
/// The catalogue is global (not tenant-scoped — every channel shares the same provider voices), so
/// <paramref name="broadcasterId"/> is accepted for interface symmetry but not filtered on.
/// <see cref="PipelineOption.SecondaryText"/> is locale, gender and provider — the fields that actually tell
/// two voices apart.
/// </summary>
internal sealed class VoiceOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public VoiceOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Voice;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<TtsVoice> query = _db.TtsVoices;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(v =>
                v.DisplayName.ToLower().Contains(term) || v.Name.ToLower().Contains(term)
            );
        }

        int total = await query.CountAsync(ct);
        List<TtsVoice> page = await query
            .OrderBy(v => v.DisplayName)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(TtsVoice voice) =>
        new(
            voice.Id,
            voice.DisplayName,
            $"{voice.Locale} · {voice.Gender} · {voice.Provider}",
            voice.PreviewUrl,
            PipelineOptionState.Selectable
        );
}
