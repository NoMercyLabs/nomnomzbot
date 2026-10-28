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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Keeps <see cref="Pipeline.GraphJsonCache"/> filled for every pipeline that has step rows. The timer
/// service and the reward handler read only that cache, so a pipeline saved with rows alone ran nothing
/// when a timer or reward fired. The graph comes from <see cref="PipelineGraphBuilder"/>, the same builder
/// the channel registry uses for chat commands.
/// </summary>
public sealed class PipelineGraphCacheHealer
{
    private readonly IApplicationDbContext _db;

    public PipelineGraphCacheHealer(IApplicationDbContext db) => _db = db;

    /// <summary>Fills the cache of every pipeline with step rows and an empty cache. Returns the count healed.</summary>
    public Task<int> HealMissingAsync(CancellationToken ct = default) =>
        HealAsync(_db.Pipelines.Where(p => p.GraphJsonCache == null || p.GraphJsonCache == ""), ct);

    /// <summary>Rebuilds the cache of the given pipelines from their step rows, for a writer that just saved rows.</summary>
    public Task<int> RebuildAsync(
        IReadOnlyCollection<Guid> pipelineIds,
        CancellationToken ct = default
    ) => HealAsync(_db.Pipelines.Where(p => pipelineIds.Contains(p.Id)), ct);

    private async Task<int> HealAsync(IQueryable<Pipeline> candidates, CancellationToken ct)
    {
        List<Pipeline> pipelines = await candidates
            .Where(p => _db.PipelineSteps.Any(s => s.PipelineId == p.Id))
            .ToListAsync(ct);
        if (pipelines.Count == 0)
            return 0;

        List<Guid> ids = [.. pipelines.Select(p => p.Id)];
        List<PipelineStep> steps = await _db
            .PipelineSteps.Where(s => ids.Contains(s.PipelineId))
            .Include(s => s.Conditions)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);
        ILookup<Guid, PipelineStep> stepsByPipeline = steps.ToLookup(s => s.PipelineId);

        foreach (Pipeline pipeline in pipelines)
        {
            string graphJson = PipelineGraphBuilder.BuildGraphJson([
                .. stepsByPipeline[pipeline.Id],
            ]);
            if (pipeline.GraphJsonCache == graphJson)
                continue;

            // The platform hash is taken from the live cache, so a row still equal to its stamp must
            // follow the new cache or it reads as customized.
            bool untouched =
                pipeline.PlatformSourceHash
                == PlatformContentHash.ComputeHash(pipeline.GraphJsonCache);
            pipeline.GraphJsonCache = graphJson;
            if (untouched)
                pipeline.PlatformSourceHash = PlatformContentHash.ComputeHash(graphJson);
        }

        await _db.SaveChangesAsync(ct);
        return pipelines.Count;
    }
}
