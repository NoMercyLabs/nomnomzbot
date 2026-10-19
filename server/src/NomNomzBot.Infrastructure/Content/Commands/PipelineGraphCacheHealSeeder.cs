// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
