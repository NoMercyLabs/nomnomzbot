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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Content.Platform;

/// <summary>
/// Seeds the global (channel-agnostic) platform configuration defaults (backend-structure
/// §5.2, Order 10 — global reference data, no FK dependencies). A global config row is one
/// with a <c>null</c> <see cref="Configuration.BroadcasterId"/>. Idempotent: upserts by the
/// natural key (<c>BroadcasterId == null</c>, <see cref="Configuration.Key"/>), so a re-run
/// adds nothing new — and never overwrites a value an operator has since edited.
/// </summary>
public sealed class ConfigSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public ConfigSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 10;

    private static readonly IReadOnlyDictionary<string, string> GlobalDefaults = new Dictionary<
        string,
        string
    >
    {
        ["system:version"] = "1.0.0",
        ["system:tts:providers"] = "edge,azure,elevenlabs",
        ["system:tts:maxDurationSeconds"] = "30",
        ["system:moderation:defaultSpamThreshold"] = "5",
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<string> existingKeys = await _db
            .Configurations.Where(c => c.BroadcasterId == null)
            .Select(c => c.Key)
            .ToListAsync(ct);
        HashSet<string> present = existingKeys.ToHashSet(StringComparer.Ordinal);

        foreach ((string key, string value) in GlobalDefaults)
        {
            if (present.Contains(key))
                continue;

            _db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = key,
                    Value = value,
                }
            );
        }
    }
}
