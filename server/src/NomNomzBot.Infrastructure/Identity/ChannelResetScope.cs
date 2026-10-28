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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The single definition of what a channel reset removes: the channel's own <see cref="Configuration"/> rows,
/// grouped into the settings areas a streamer recognises. The reset deletes through <see cref="RemoveAsync"/>
/// and the preview counts through <see cref="CountByAreaAsync"/>; both read <see cref="Rows"/>, so the two
/// cannot drift apart. Rows whose key matches no area land in the <c>other</c> remainder rather than being
/// dropped from the count.
/// </summary>
public static class ChannelResetScope
{
    // Ordered areas; the remainder is always last. A key belongs to the first area whose prefix it starts with.
    private static readonly IReadOnlyList<(string CategoryKey, string[] Prefixes)> Areas =
    [
        (BlastRadiusCategoryKeys.ResetChat, ["chat.", "chat:", "render."]),
        (BlastRadiusCategoryKeys.ResetTts, ["tts"]),
        (BlastRadiusCategoryKeys.ResetModeration, ["ban:", "shield", "blocked", "moderation"]),
        (BlastRadiusCategoryKeys.ResetMusic, ["music:", "spotify."]),
        (BlastRadiusCategoryKeys.ResetRaids, ["raids:"]),
        (BlastRadiusCategoryKeys.ResetCredentials, ["provider:", "youtube.", "patreon.", "auth."]),
    ];

    /// <summary>Every category the reset can report, remainder last.</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } =
    [.. Areas.Select(area => area.CategoryKey), BlastRadiusCategoryKeys.ResetOther];

    /// <summary>The rows a reset of this channel removes. Another channel's and the global rows are not in it.</summary>
    public static IQueryable<Configuration> Rows(IApplicationDbContext db, Guid broadcasterId) =>
        db.Configurations.Where(c => c.BroadcasterId == broadcasterId);

    /// <summary>The settings area one configuration key is reported under.</summary>
    public static string CategoryOf(string key)
    {
        foreach ((string categoryKey, string[] prefixes) in Areas)
        {
            if (prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                return categoryKey;
        }

        return BlastRadiusCategoryKeys.ResetOther;
    }

    /// <summary>Counts what <see cref="RemoveAsync"/> would remove, per area, omitting empty areas.</summary>
    public static async Task<IReadOnlyList<BlastRadiusCategoryDto>> CountByAreaAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<string> keys = await Rows(db, broadcasterId)
            .Select(c => c.Key)
            .ToListAsync(cancellationToken);

        Dictionary<string, int> counts = keys.GroupBy(CategoryOf)
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. CategoryOrder
                .Where(counts.ContainsKey)
                .Select(key => new BlastRadiusCategoryDto(key, counts[key], [])),
        ];
    }

    /// <summary>Removes the channel's configuration rows and returns how many were removed.</summary>
    public static async Task<int> RemoveAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<Configuration> rows = await Rows(db, broadcasterId).ToListAsync(cancellationToken);

        db.Configurations.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        return rows.Count;
    }
}
