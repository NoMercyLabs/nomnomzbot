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
