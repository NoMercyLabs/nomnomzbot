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

using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Startup pass that fills the compiled graph of every pipeline saved with step rows only, so timers and
/// rewards bound to it run. Order 88: after the flow seeders (82-87) and the platform stamping (85); the
/// healer restamps the platform hash itself. Idempotent: a filled cache is never touched. The whole seed
/// pass already runs under the run-once guard.
/// </summary>
public sealed class PipelineGraphCacheHealSeeder : ISeeder
{
    private readonly PipelineGraphCacheHealer _healer;

    public PipelineGraphCacheHealSeeder(PipelineGraphCacheHealer healer) => _healer = healer;

    public int Order => 88;

    public async Task SeedAsync(CancellationToken ct = default) =>
        await _healer.HealMissingAsync(ct);
}
