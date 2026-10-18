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
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.CustomCode.Pipeline;

/// <summary>
/// Supplies the channel's custom-code scripts for the <c>script</c> resource-picker kind, so a Run script step
/// picks a script by name. The option value is the script's Guid id (the stored form); a saved value in the
/// 26-character ULID wire form is decoded before the lookup.
/// </summary>
internal sealed class ScriptOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public ScriptOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Script;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<CodeScript> query = _db.CodeScripts.Where(s => s.BroadcasterId == broadcasterId);
        if (request.IsValueLookup)
        {
            Guid valueId = OwnedIdCodec.TryDecode(request.Value, out Guid decoded)
                ? decoded
                : Guid.Empty;
            query = query.Where(s => s.Id == valueId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.ToLowerInvariant();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<CodeScript> page = await query
            .OrderBy(s => s.Name)
            .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
            .Take(request.Pagination.PageSize)
            .ToListAsync(ct);

        List<PipelineOption> options = [.. page.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(CodeScript script) =>
        new(
            script.Id.ToString(),
            script.Name,
            script.Description ?? script.Language,
            ImageUrl: null,
            script.IsEnabled ? PipelineOptionState.Selectable : PipelineOptionState.Unavailable,
            script.IsEnabled ? null : "Script is disabled."
        );
}
