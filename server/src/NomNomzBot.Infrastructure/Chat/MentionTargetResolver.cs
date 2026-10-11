// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Resolves a typed chat target to a login: a login-shaped name needs no lookup, any other name is matched
/// against the display names of known users (case-insensitive on both Postgres and SQLite).
/// </summary>
public sealed partial class MentionTargetResolver(IApplicationDbContext db) : IMentionTargetResolver
{
    [GeneratedRegex("^[a-zA-Z0-9_]{1,25}$")]
    private static partial Regex LoginPattern();

    public async Task<string> ResolveLoginAsync(string? rawTarget, CancellationToken ct = default)
    {
        string parsed = MentionParser.ParseUserMention(rawTarget);
        if (parsed.Length == 0)
            return parsed;
        if (LoginPattern().IsMatch(parsed))
            return parsed.ToLowerInvariant();

        string lowered = parsed.ToLower();
        List<string> logins = await db
            .Users.AsNoTracking()
            .Where(u => u.DisplayName.ToLower() == lowered)
            .Select(u => u.Username)
            .Take(2)
            .ToListAsync(ct);
        return logins.Count == 1 ? logins[0] : parsed;
    }
}
