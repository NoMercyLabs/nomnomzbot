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
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// Creates the <see cref="PlatformContentDefinition"/>/<see cref="PlatformContentVersion"/> v1 rows for the
/// system commands <see cref="DefaultCommandsSeeder"/> already ships (<c>sr</c>, <c>skip</c>, <c>queue</c>,
/// <c>volume</c>, <c>song</c>), then backfills every existing <see cref="ChannelBuiltinCommand"/> row for
/// those keys that has no provenance yet — the platform-admin.md §7 exit condition: without this pass every
/// pre-existing tenant row reads as tenant-authored (<c>PlatformSourceDefinitionId = null</c>) and is
/// silently excluded from every future <c>update_in_place_where_untouched</c> publish.
/// </summary>
/// <remarks>
/// The default payload is the "no overrides" shape (<c>OverridesJson = null</c>, canonicalized to
/// <c>"{}"</c>) — exactly what every tenant row the seeder ever wrote actually holds, customized or not.
/// Backfilling ALL matching rows with the SAME definition/version/hash (rather than each row's OWN current
/// hash) is deliberate: it is what makes a customized tenant's current-content hash differ from its stored
/// provenance hash later, which is the untouched-detection this whole slice exists to support (§2.1).
/// Idempotent: only touches rows with <c>PlatformSourceDefinitionId == null</c> — the seeder principle's
/// "adopt an empty stub, never overwrite" rule applied to backfill. Order 81 — after
/// <see cref="DefaultCommandsSeeder"/> (80), which the backfill half depends on.
/// </remarks>
public sealed class PlatformContentDefinitionSeeder : ISeeder
{
    /// <summary>The "no overrides" default payload every <see cref="DefaultCommandsSeeder"/> row ships with.</summary>
    private const string DefaultPayloadJson = "{}";

    private static readonly string[] DefaultKeys = ["sr", "skip", "queue", "volume", "song"];

    private readonly IApplicationDbContext _db;

    public PlatformContentDefinitionSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 81;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        string defaultHash = PlatformContentHash.ComputeHash(DefaultPayloadJson);

        Dictionary<string, PlatformContentDefinition> existingDefinitions = await _db
            .PlatformContentDefinitions.Where(d =>
                d.Kind == PlatformContentKinds.Command && DefaultKeys.Contains(d.Key)
            )
            .ToDictionaryAsync(d => d.Key, StringComparer.Ordinal, ct);

        Dictionary<Guid, PlatformContentDefinition> definitionsById = [];

        foreach (string key in DefaultKeys)
        {
            if (existingDefinitions.TryGetValue(key, out PlatformContentDefinition? definition))
            {
                definitionsById[definition.Id] = definition;
                continue;
            }

            DateTime now = DateTime.UtcNow;
            PlatformContentDefinition created = new()
            {
                Kind = PlatformContentKinds.Command,
                Key = key,
                DisplayName = key,
                CreatedAt = now,
                // No IamPrincipal exists for a system seed pass — Guid.Empty marks "seeded, not authored by
                // a human operator", mirroring how other global-reference seeders have no acting principal.
                CreatedByPrincipalId = Guid.Empty,
            };
            _db.PlatformContentDefinitions.Add(created);

            PlatformContentVersion version = new()
            {
                DefinitionId = created.Id,
                Version = 1,
                ContentHash = defaultHash,
                PayloadJson = DefaultPayloadJson,
                DraftedAt = now,
                DraftedByPrincipalId = Guid.Empty,
                PublishedAt = now,
                PublishedByPrincipalId = Guid.Empty,
            };
            _db.PlatformContentVersions.Add(version);

            created.CurrentVersionId = version.Id;
            created.LatestDraftVersionId = version.Id;

            definitionsById[created.Id] = created;
        }

        await _db.SaveChangesAsync(ct);

        Dictionary<string, PlatformContentDefinition> definitionsByKey = definitionsById.Values.ToDictionary(
            d => d.Key,
            StringComparer.Ordinal
        );

        List<ChannelBuiltinCommand> unstamped = await _db
            .ChannelBuiltinCommands.Where(b =>
                DefaultKeys.Contains(b.BuiltinKey) && b.PlatformSourceDefinitionId == null
            )
            .ToListAsync(ct);

        if (unstamped.Count == 0)
            return;

        DateTime syncedAt = DateTime.UtcNow;
        foreach (ChannelBuiltinCommand row in unstamped)
        {
            if (!definitionsByKey.TryGetValue(row.BuiltinKey, out PlatformContentDefinition? definition))
                continue;

            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = 1;
            row.PlatformSourceHash = defaultHash;
            row.PlatformSourceSyncedAt = syncedAt;
        }

        await _db.SaveChangesAsync(ct);
    }
}
