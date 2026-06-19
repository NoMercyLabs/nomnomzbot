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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// Seeds default music commands (!sr, !skip, !queue, !volume, !song) for every enabled channel
/// that does not already have them. Runs once at startup.
/// All commands are of pipeline type so they are fully configurable per-channel.
/// </summary>
public sealed class DefaultCommandSeederService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultCommandSeederService> _logger;

    private static readonly IReadOnlyList<DefaultCommand> Defaults =
    [
        new(
            "!sr",
            """{"steps":[{"action":{"type":"music_request"}}]}""",
            "everyone",
            5,
            "Request a song"
        ),
        new(
            "!skip",
            """{"steps":[{"action":{"type":"music_skip"}}]}""",
            "moderator",
            0,
            "Skip the current song"
        ),
        new(
            "!queue",
            """{"steps":[{"action":{"type":"music_queue"}}]}""",
            "everyone",
            10,
            "Show the song queue"
        ),
        new(
            "!volume",
            """{"steps":[{"action":{"type":"music_volume"}}]}""",
            "moderator",
            0,
            "Set the music volume"
        ),
        new(
            "!song",
            """{"steps":[{"action":{"type":"music_current"}}]}""",
            "everyone",
            5,
            "Show the current song"
        ),
    ];

    public DefaultCommandSeederService(
        IServiceScopeFactory scopeFactory,
        ILogger<DefaultCommandSeederService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            List<string> channelIds = await db
                .Channels.Where(c => c.DeletedAt == null)
                .Select(c => c.Id)
                .ToListAsync(stoppingToken);

            int seeded = 0;
            foreach (string channelId in channelIds)
            {
                foreach (DefaultCommand def in Defaults)
                {
                    bool exists = await db.Commands.AnyAsync(
                        c => c.BroadcasterId == channelId && c.Name == def.Name,
                        stoppingToken
                    );

                    if (exists)
                        continue;

                    db.Commands.Add(
                        new()
                        {
                            BroadcasterId = channelId,
                            Name = def.Name,
                            Type = "pipeline",
                            PipelineJson = def.PipelineJson,
                            Permission = def.Permission,
                            CooldownSeconds = def.CooldownSeconds,
                            Description = def.Description,
                            IsEnabled = true,
                        }
                    );
                    seeded++;
                }
            }

            if (seeded > 0)
            {
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation(
                    "DefaultCommandSeeder: seeded {Count} default commands across {Channels} channels",
                    seeded,
                    channelIds.Count
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DefaultCommandSeeder failed");
        }
    }

    private sealed record DefaultCommand(
        string Name,
        string PipelineJson,
        string Permission,
        int CooldownSeconds,
        string Description
    );
}
