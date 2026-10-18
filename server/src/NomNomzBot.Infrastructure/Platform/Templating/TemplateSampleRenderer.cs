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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// Resolves the picker's live examples through <see cref="ITemplateResolver"/> itself, with the signed-in
/// user seeded the way a chat message seeds its sender, so an example can never drift from what the engine
/// renders. One resolve call covers every live key.
/// </summary>
public sealed class TemplateSampleRenderer(IApplicationDbContext db, ITemplateResolver resolver)
    : ITemplateSampleRenderer
{
    // A control character no helper key or rendered value contains, so the joined result splits back apart.
    private const char Separator = '\u001f';

    public async Task<IReadOnlyList<TemplateHelperEntry>> PersonalizeAsync(
        IReadOnlyList<TemplateHelperEntry> entries,
        Guid broadcasterId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> liveKeys =
        [
            .. entries
                .Where(e => e.Prefix is null && TemplateHelperSamples.LiveKeys.Contains(e.Key))
                .Select(e => e.Key),
        ];
        if (liveKeys.Count == 0)
            return entries;

        User? user = await db
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return entries;

        Dictionary<string, string> seeds = BuildUserSeeds(user);
        string template = string.Join(Separator, liveKeys.Select(k => "{" + k + "}"));
        string rendered = await resolver.ResolveAsync(
            template,
            seeds,
            broadcasterId,
            cancellationToken
        );

        string[] values = rendered.Split(Separator);
        Dictionary<string, string> realByKey = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < liveKeys.Count && i < values.Length; i++)
        {
            // The engine leaves a placeholder it cannot fill as-is, and renders an unset one as empty:
            // neither is an example, so the neutral sample stays.
            bool unresolved = values[i].Length == 0 || values[i] == "{" + liveKeys[i] + "}";
            if (!unresolved)
                realByKey[liveKeys[i]] = values[i];
        }

        return
        [
            .. entries.Select(e =>
                realByKey.TryGetValue(e.Key, out string? real) && e.Prefix is null
                    ? e with
                    {
                        Sample = real,
                    }
                    : e
            ),
        ];
    }

    /// <summary>The same variables a chat message seeds for its sender: display name, login, platform id.</summary>
    private static Dictionary<string, string> BuildUserSeeds(User user)
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = user.DisplayName,
            ["user.name"] = user.Username,
        };

        if (!string.IsNullOrEmpty(user.TwitchUserId))
        {
            seeds["user.id"] = user.TwitchUserId;
            seeds["user.provider"] = "twitch";
        }

        return seeds;
    }
}
