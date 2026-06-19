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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Seeds the shipped pronoun reference set (backend-structure §5.2, Order 10 — global
/// reference data, no FK dependencies). Idempotent: upserts by the pronoun's natural key
/// <see cref="Pronoun.Name"/> (e.g. <c>they/them</c>), so a re-run adds nothing new.
/// </summary>
public sealed class PronounSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public PronounSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 10;

    private static readonly IReadOnlyList<Pronoun> Pronouns =
    [
        new()
        {
            Name = "they/them",
            Subject = "they",
            Object = "them",
            Singular = false,
        },
        new()
        {
            Name = "she/her",
            Subject = "she",
            Object = "her",
            Singular = true,
        },
        new()
        {
            Name = "he/him",
            Subject = "he",
            Object = "him",
            Singular = true,
        },
        new()
        {
            Name = "she/they",
            Subject = "she",
            Object = "them",
            Singular = false,
        },
        new()
        {
            Name = "he/they",
            Subject = "he",
            Object = "them",
            Singular = false,
        },
        new()
        {
            Name = "any/all",
            Subject = "any",
            Object = "all",
            Singular = false,
        },
        new()
        {
            Name = "other/ask",
            Subject = "other",
            Object = "ask",
            Singular = false,
        },
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<string> existingNames = await _db.Pronouns.Select(p => p.Name).ToListAsync(ct);
        HashSet<string> present = existingNames.ToHashSet(StringComparer.Ordinal);

        foreach (Pronoun pronoun in Pronouns)
        {
            if (present.Contains(pronoun.Name))
                continue;

            _db.Pronouns.Add(
                new()
                {
                    Name = pronoun.Name,
                    Subject = pronoun.Subject,
                    Object = pronoun.Object,
                    Singular = pronoun.Singular,
                }
            );
        }
    }
}
