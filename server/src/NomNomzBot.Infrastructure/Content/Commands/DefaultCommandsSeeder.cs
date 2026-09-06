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

// S-SEED-GUIDCASE: Microsoft.Data.Sqlite binds Guid parameters as canonical uppercase-hyphenated
// text; SQLite text comparison is case-sensitive. A Channels.Id row written in any other case (a raw
// import, a Postgres-to-SQLite migration) is invisible to a parameterized Guid comparison, so the FK
// check on an insert referencing it fails. Postgres has a native uuid type and never carries this.
// Fixed here at the seeder boundary (scoped self-heal, see NormalizeChannelGuidCasingAsync below) —
// the same trap sits under every other Guid equality/Contains predicate SQLite translates to SQL,
// most notably the tenant global query filter (ModelBuilderExtensions.ApplyTenantAndSoftDeleteFilters).
// Not fixed there in this slice — tracked as follow-up.

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
        await NormalizeChannelGuidCasingAsync(ct);

        List<Guid> channelIds = broadcasterId is { } id
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

        HashSet<(Guid, string)> present = [.. existing];

        foreach (Guid channelId in channelIds)
        {
            foreach (string key in DefaultKeys)
            {
                if (present.Contains((channelId, key)))
                    continue;

                _db.ChannelBuiltinCommands.Add(
                    new()
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

    /// <summary>
    /// Self-heals a non-canonical Channels.Id (or a ChannelBuiltinCommands.BroadcasterId already written
    /// to match it) to Microsoft.Data.Sqlite's own canonical uppercase-hyphenated text, so the FK insert
    /// below never trips a case-only mismatch. A no-op on Postgres (native uuid type, no text casing to
    /// normalize) and a no-op once every row is already canonical.
    ///
    /// S-TENANT-GUIDCASE gave every Guid(?) column a NOCASE collation, so the plain <c>&lt;&gt;</c> below
    /// would otherwise consider a lower-cased Id and its own <c>upper(Id)</c> EQUAL (same letters, different
    /// case) and never select it — the self-heal would silently stop healing anything. The explicit
    /// <c>COLLATE BINARY</c> overrides the column's declared collation for this one comparison, restoring the
    /// ordinal case check this repair pass depends on.
    /// </summary>
    private async Task NormalizeChannelGuidCasingAsync(CancellationToken ct)
    {
        if (_db is not DbContext dbContext || !dbContext.Database.IsSqlite())
            return;

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Channels SET Id = upper(Id) WHERE Id <> upper(Id) COLLATE BINARY",
            ct
        );
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE ChannelBuiltinCommands SET BroadcasterId = upper(BroadcasterId) WHERE BroadcasterId <> upper(BroadcasterId) COLLATE BINARY",
            ct
        );
    }
}
