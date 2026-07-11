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

    // BARE keys — the canonical ChannelBuiltinCommand format the dashboard/BuiltinCommandService write
    // (item 24c: the seeder used to write bang-prefixed keys, orphaning seeded rows from the toggle UI;
    // the NormalizeBuiltinKeys migration repaired the old rows).
    private static readonly string[] DefaultKeys = ["sr", "skip", "queue", "volume", "song"];

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>
    /// Seeds the default builtins for a single channel (<paramref name="broadcasterId"/>) or, when null, every
    /// channel. <c>DefaultCommandsSeedOnOnboardingHandler</c> (Content.Commands.EventHandlers) calls this
    /// scoped to the newly-onboarded channel so it does not have to wait for the next full-startup pass; same
    /// idempotent upsert-by-natural-key either way.
    /// </summary>
    public async Task SeedAsync(Guid? broadcasterId, CancellationToken ct = default)
    {
        List<Guid> channelIds = broadcasterId is Guid id
            ? [id]
            : await _db.Channels.Select(c => c.Id).ToListAsync(ct);

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
