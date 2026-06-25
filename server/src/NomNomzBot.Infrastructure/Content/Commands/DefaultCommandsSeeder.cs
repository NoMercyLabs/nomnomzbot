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
/// Seeds the shipped built-in music commands (<c>!sr</c>, <c>!skip</c>, <c>!queue</c>,
/// <c>!volume</c>, <c>!song</c>) as <see cref="ChannelBuiltinCommand"/> rows for every
/// channel that does not already have them.
/// </summary>
/// <remarks>
/// Idempotent: upserts by the natural key <c>(BroadcasterId, BuiltinKey)</c>.
/// Order 80 — last, because it FK-references Channel rows created at runtime by onboarding.
/// </remarks>
public sealed class DefaultCommandsSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public DefaultCommandsSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 80;

    private static readonly string[] DefaultKeys = ["!sr", "!skip", "!queue", "!volume", "!song"];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Guid> channelIds = await _db.Channels.Select(c => c.Id).ToListAsync(ct);

        if (channelIds.Count == 0)
            return;

        List<(Guid BroadcasterId, string Key)> existing = await _db
            .ChannelBuiltinCommands.Where(b =>
                channelIds.Contains(b.BroadcasterId) && DefaultKeys.Contains(b.BuiltinKey)
            )
            .Select(b => new ValueTuple<Guid, string>(b.BroadcasterId, b.BuiltinKey))
            .ToListAsync(ct);

        HashSet<(Guid, string)> present = existing.ToHashSet();

        foreach (Guid channelId in channelIds)
        {
            foreach (string key in DefaultKeys)
            {
                if (present.Contains((channelId, key)))
                    continue;

                _db.ChannelBuiltinCommands.Add(
                    new ChannelBuiltinCommand
                    {
                        BroadcasterId = channelId,
                        BuiltinKey = key,
                        IsEnabled = true,
                    }
                );
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
