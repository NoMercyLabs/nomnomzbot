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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Startup pass that rebinds surface rows from wrapper pipelines to the command they wrapped (spec
/// commands-pipelines 12.6). Order 89: after the graph-cache healer (88), so a freshly filled graph is
/// judged. Once per data set: the migrator records a global marker row, so a wrapper pipeline a user picks as
/// a binding target after the first pass is never rebound at a later boot. The whole seed pass already runs
/// under the run-once guard.
/// </summary>
public sealed class WrapperPipelineBindingSeeder : ISeeder
{
    private readonly WrapperPipelineMigrator _migrator;
    private readonly ILogger<WrapperPipelineBindingSeeder> _logger;

    public WrapperPipelineBindingSeeder(
        WrapperPipelineMigrator migrator,
        ILogger<WrapperPipelineBindingSeeder> logger
    )
    {
        _migrator = migrator;
        _logger = logger;
    }

    public int Order => 89;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        WrapperMigrationReport report = await _migrator.RunAsync(ct);
        if (report.Rebound.Count == 0 && report.Kept.Count == 0 && report.StreamDeck.Count == 0)
            return;

        _logger.LogInformation(
            "Wrapper pipeline migration: {Rebound} rows rebound to their command, {StreamDeck} Stream Deck key wrappers switched to the action target, {Kept} pipelines kept",
            report.Rebound.Count,
            report.StreamDeck.Count,
            report.Kept.Count
        );
    }
}
