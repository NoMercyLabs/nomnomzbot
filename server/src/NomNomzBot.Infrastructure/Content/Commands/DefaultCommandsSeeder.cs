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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Seeds the shipped default music commands (<c>!sr</c>, <c>!skip</c>, <c>!queue</c>,
/// <c>!volume</c>, <c>!song</c>) for every channel that does not already have them
/// (backend-structure §5.2 pattern, Order 80 — last, because it FK-references the
/// <see cref="Channel"/> rows created at runtime by onboarding, not by an earlier seeder).
/// A default command is just a pre-seeded pipeline command the streamer then edits or disables.
/// </summary>
/// <remarks>
/// Idempotent: upserts by the natural key <c>(BroadcasterId, Name)</c> — the same unique index
/// the schema enforces — so a re-run, and the no-channel fresh-deploy case, both add nothing.
/// </remarks>
public sealed class DefaultCommandsSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public DefaultCommandsSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 80;

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

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Guid> channelIds = await _db.Channels.Select(c => c.Id).ToListAsync(ct);

        if (channelIds.Count == 0)
            return;

        string[] defaultNames = Defaults.Select(d => d.Name).ToArray();

        // One round-trip: which (channel, name) pairs already exist among the defaults.
        List<(Guid BroadcasterId, string Name)> existing = await _db
            .Commands.Where(c =>
                channelIds.Contains(c.BroadcasterId) && defaultNames.Contains(c.Name)
            )
            .Select(c => new ValueTuple<Guid, string>(c.BroadcasterId, c.Name))
            .ToListAsync(ct);

        HashSet<(Guid, string)> present = existing.ToHashSet();

        foreach (Guid channelId in channelIds)
        {
            foreach (DefaultCommand def in Defaults)
            {
                if (present.Contains((channelId, def.Name)))
                    continue;

                _db.Commands.Add(
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
            }
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
