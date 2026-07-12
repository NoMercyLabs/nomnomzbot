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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Seeds the pronoun reference set (backend-structure §5.2, Order 10 — global reference data, no FK
/// dependencies). On boot it fetches the live set from the alejo.io pronoun API so the table tracks the
/// upstream source the Twitch pronoun plugins read; if that fetch fails or is empty (e.g. a first boot with
/// no network) it upserts a bundled fallback so the table is never empty. The combos (he/they, she/they,
/// he/she) are ALWAYS upserted from the bundle — the API does not list them (alejo generates them from
/// pronoun pairs). Idempotent: upserts by the natural key <see cref="Pronoun.Name"/> (the slash form, e.g.
/// <c>they/them</c>) — a re-run updates changed rows and adds new ones, never duplicates and never deletes.
/// The fetch is best-effort and cannot crash boot — <see cref="IAlejoPronounClient.FetchAsync"/> never throws.
/// </summary>
public sealed class PronounSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly IAlejoPronounClient _alejoClient;

    public PronounSeeder(IApplicationDbContext db, IAlejoPronounClient alejoClient)
    {
        _db = db;
        _alejoClient = alejoClient;
    }

    public int Order => 10;

    /// <summary>
    /// The combination pronouns the alejo endpoint does NOT return (it generates them from pronoun pairs),
    /// so they are always upserted from here regardless of whether the live fetch succeeds.
    /// </summary>
    private static readonly IReadOnlyList<Pronoun> Combos =
    [
        new()
        {
            Name = "she/they",
            Subject = "she",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        },
        new()
        {
            Name = "he/they",
            Subject = "he",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        },
        new()
        {
            Name = "he/she",
            Subject = "he",
            Object = "she",
            Possessive = "her",
            GenderedTerm = "person",
            Singular = false,
        },
    ];

    /// <summary>
    /// The offline fallback — the full shipped pronoun set (base pronouns + combos). Upserted only when the
    /// live alejo fetch fails or returns nothing, so a first boot with no network still populates the table.
    /// </summary>
    private static readonly IReadOnlyList<Pronoun> Fallback =
    [
        new()
        {
            Name = "they/them",
            Subject = "they",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        },
        new()
        {
            Name = "she/her",
            Subject = "she",
            Object = "her",
            Possessive = "her",
            GenderedTerm = "gal",
            Singular = true,
        },
        new()
        {
            Name = "he/him",
            Subject = "he",
            Object = "him",
            Possessive = "his",
            GenderedTerm = "guy",
            Singular = true,
        },
        new()
        {
            Name = "any/all",
            Subject = "any",
            Object = "all",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        },
        new()
        {
            Name = "other/ask",
            Subject = "other",
            Object = "ask",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        },
        // The neopronoun set from the alejo.io pronoun API (api.pronouns.alejo.io/v1/pronouns) — what the
        // Twitch pronoun plugins read. Single neopronouns are singular (Singular = true). Possessive is the
        // determiner form from the standard gender-neutral pronoun chart (e.g. "check out {possessive} stream").
        new()
        {
            Name = "ae/aer",
            Subject = "ae",
            Object = "aer",
            Possessive = "aer",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "e/em",
            Subject = "e",
            Object = "em",
            Possessive = "eir",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "fae/faer",
            Subject = "fae",
            Object = "faer",
            Possessive = "faer",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "it/its",
            Subject = "it",
            Object = "its",
            Possessive = "its",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "per/per",
            Subject = "per",
            Object = "per",
            Possessive = "per",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "ve/ver",
            Subject = "ve",
            Object = "ver",
            Possessive = "vis",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "xe/xem",
            Subject = "xe",
            Object = "xem",
            Possessive = "xyr",
            GenderedTerm = "person",
            Singular = true,
        },
        new()
        {
            Name = "zie/hir",
            Subject = "zie",
            Object = "hir",
            Possessive = "hir",
            GenderedTerm = "person",
            Singular = true,
        },
        .. Combos,
    ];

    /// <summary>
    /// Possessive determiner + neutral gendered term for the subjects the live alejo fetch can return,
    /// keyed by <see cref="Pronoun.Subject"/> (alejo never returns the combos — those come from
    /// <see cref="Combos"/> only). A subject alejo adds later that isn't in this table still gets a safe,
    /// always-correct default (see <see cref="Map"/>) rather than an empty/fabricated value.
    /// </summary>
    private static readonly IReadOnlyDictionary<
        string,
        (string Possessive, string GenderedTerm)
    > KnownGrammar = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["he"] = ("his", "guy"),
        ["she"] = ("her", "gal"),
        ["they"] = ("their", "person"),
        ["any"] = ("their", "person"),
        ["other"] = ("their", "person"),
        ["ae"] = ("aer", "person"),
        ["e"] = ("eir", "person"),
        ["fae"] = ("faer", "person"),
        ["it"] = ("its", "person"),
        ["per"] = ("per", "person"),
        ["ve"] = ("vis", "person"),
        ["xe"] = ("xyr", "person"),
        ["zie"] = ("hir", "person"),
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PronounRecord>? fetched = await _alejoClient.FetchAsync(ct);

        IReadOnlyList<Pronoun> source = fetched is { Count: > 0 }
            // Live set: the mapped fetched pronouns plus the combos the API never lists.
            ? [.. fetched.Select(Map), .. Combos]
            // Offline: the bundled fallback (which already includes the combos).
            : Fallback;

        await UpsertAsync(source, ct);
    }

    /// <summary>
    /// Upserts each pronoun by its natural key <see cref="Pronoun.Name"/>: updates the columns of an existing
    /// row when they differ, inserts a new row when the name is absent. Never deletes — a name dropped upstream
    /// stays. Tracked changes are committed by the <c>SeedRunner</c>'s single transaction.
    /// </summary>
    private async Task UpsertAsync(IReadOnlyList<Pronoun> source, CancellationToken ct)
    {
        List<Pronoun> existing = await _db.Pronouns.ToListAsync(ct);
        Dictionary<string, Pronoun> byName = existing.ToDictionary(
            p => p.Name,
            StringComparer.Ordinal
        );

        // De-dup the source by name (last wins) so a name appearing twice cannot insert a duplicate row.
        Dictionary<string, Pronoun> desired = new(StringComparer.Ordinal);
        foreach (Pronoun pronoun in source)
            desired[pronoun.Name] = pronoun;

        foreach (Pronoun pronoun in desired.Values)
        {
            if (byName.TryGetValue(pronoun.Name, out Pronoun? current))
            {
                // Update in place only when something actually changed (keeps the change-tracker quiet on a no-op re-run).
                if (
                    current.Subject != pronoun.Subject
                    || current.Object != pronoun.Object
                    || current.Possessive != pronoun.Possessive
                    || current.GenderedTerm != pronoun.GenderedTerm
                    || current.Singular != pronoun.Singular
                    || current.Key != pronoun.Key
                )
                {
                    current.Subject = pronoun.Subject;
                    current.Object = pronoun.Object;
                    current.Possessive = pronoun.Possessive;
                    current.GenderedTerm = pronoun.GenderedTerm;
                    current.Singular = pronoun.Singular;
                    current.Key = pronoun.Key;
                }
            }
            else
            {
                _db.Pronouns.Add(
                    new()
                    {
                        Name = pronoun.Name,
                        Subject = pronoun.Subject,
                        Object = pronoun.Object,
                        Possessive = pronoun.Possessive,
                        GenderedTerm = pronoun.GenderedTerm,
                        Singular = pronoun.Singular,
                        Key = pronoun.Key,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Maps an alejo record to a <see cref="Pronoun"/>. The natural key/display <see cref="Pronoun.Name"/> is
    /// the slash form (<c>they/them</c>); a singleton whose subject and object are the same word (<c>any</c>,
    /// <c>other</c>) collapses to just that word. Subject/Object are lowercased to match the bundled set.
    /// </summary>
    private static Pronoun Map(PronounRecord record)
    {
        string subject = record.Subject.ToLowerInvariant();
        string @object = record.Object.ToLowerInvariant();
        string name = subject == @object ? subject : $"{subject}/{@object}";

        // The alejo payload carries no grammar beyond subject/object/singular. Known subjects get their
        // real possessive + gendered term from the chart above; an upstream addition we don't recognize
        // yet still gets a safe, always-grammatical default rather than an empty column.
        (string possessive, string genderedTerm) = KnownGrammar.TryGetValue(
            subject,
            out (string Possessive, string GenderedTerm) known
        )
            ? known
            : ("their", "person");

        return new Pronoun
        {
            Name = name,
            Subject = subject,
            Object = @object,
            Possessive = possessive,
            GenderedTerm = genderedTerm,
            Singular = record.Singular,
            Key = record.Key,
        };
    }
}
