// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the pronoun sync (alejo-fetch-then-upsert with a bundled offline fallback). When the live fetch
/// succeeds it seeds the mapped upstream set PLUS the combos the API never lists, and a re-run upserts in
/// place with zero duplicates. When the fetch fails (no network) it seeds the full bundled fallback so the
/// table is never empty — and the seed never throws out of boot.
/// </summary>
public sealed class PronounSeederTests
{
    private static SeedTestDbContext NewContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<SeedTestDbContext>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );

    // ── The fetched set is mapped + the combos are always added ──────────────

    [Fact]
    public async Task Seeds_the_fetched_pronouns_and_the_bundled_combos()
    {
        string db = Guid.NewGuid().ToString();

        // A live set with a two-part (theythem) and a singleton (any) — the combos are NOT in this payload.
        StubAlejoClient client = new([
            new PronounRecord("They", "Them", false, "theythem"),
            new PronounRecord("Any", "Any", true, "any"),
        ]);

        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(context, client).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using SeedTestDbContext verify = NewContext(db);
        List<Pronoun> all = await verify.Pronouns.ToListAsync();

        // The two fetched pronouns, mapped to the slash/collapsed form.
        Pronoun theyThem = all.Single(p => p.Name == "they/them");
        theyThem.Subject.Should().Be("they");
        theyThem.Object.Should().Be("them");
        theyThem.Singular.Should().BeFalse();

        Pronoun any = all.Single(p => p.Name == "any");
        any.Subject.Should().Be("any");
        any.Object.Should().Be("any");
        any.Singular.Should().BeTrue();

        // The three combos the API never lists are ALWAYS seeded from the bundle.
        all.Select(p => p.Name).Should().Contain(["she/they", "he/they", "he/she"]);

        // The live set + the three combos, nothing else.
        all.Should().HaveCount(5);
    }

    [Fact]
    public async Task Reseeding_the_live_set_is_idempotent_and_adds_no_duplicates()
    {
        string db = Guid.NewGuid().ToString();
        StubAlejoClient client = new([new PronounRecord("They", "Them", false, "theythem")]);

        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(context, client).SeedAsync();
            await context.SaveChangesAsync();
        }

        int afterFirst;
        await using (SeedTestDbContext context = NewContext(db))
            afterFirst = await context.Pronouns.CountAsync();

        // Second pass over the SAME data — every name already present, so nothing is added.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(context, client).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using SeedTestDbContext verify = NewContext(db);
        (await verify.Pronouns.CountAsync()).Should().Be(afterFirst);
        verify.Pronouns.Select(p => p.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Reseeding_updates_a_changed_row_in_place()
    {
        string db = Guid.NewGuid().ToString();

        // First the upstream reports they/them as plural...
        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(
                context,
                new StubAlejoClient([new PronounRecord("They", "Them", false, "theythem")])
            ).SeedAsync();
            await context.SaveChangesAsync();
        }

        // ...then a later fetch flips the Singular flag for the same name.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(
                context,
                new StubAlejoClient([new PronounRecord("They", "Them", true, "theythem")])
            ).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using SeedTestDbContext verify = NewContext(db);
        Pronoun theyThem = await verify.Pronouns.SingleAsync(p => p.Name == "they/them");
        theyThem
            .Singular.Should()
            .BeTrue("the upsert updates the changed row in place, not a duplicate");
        (await verify.Pronouns.CountAsync(p => p.Name == "they/them")).Should().Be(1);
    }

    // ── Fallback path: a no-network fetch seeds the bundled set, never throws ──

    [Fact]
    public async Task Falls_back_to_the_bundled_set_when_the_fetch_returns_null()
    {
        string db = Guid.NewGuid().ToString();
        OfflineAlejoClient offline = new(); // FetchAsync → null (network failure)

        Func<Task> act = async () =>
        {
            await using SeedTestDbContext context = NewContext(db);
            await new PronounSeeder(context, offline).SeedAsync();
            await context.SaveChangesAsync();
        };

        // The seed completes (no throw out of boot) and the table is populated from the bundle.
        await act.Should().NotThrowAsync();

        await using SeedTestDbContext verify = NewContext(db);
        List<Pronoun> all = await verify.Pronouns.ToListAsync();
        all.Should().HaveCount(16, "the bundled fallback ships sixteen pronouns");
        // The fallback is self-sufficient: base pronouns + the combos are all present.
        all.Select(p => p.Name)
            .Should()
            .Contain([
                "they/them",
                "she/her",
                "he/him",
                "any/all",
                "she/they",
                "he/they",
                "he/she",
            ]);
    }

    [Fact]
    public async Task Falls_back_to_the_bundled_set_when_the_fetch_returns_empty()
    {
        string db = Guid.NewGuid().ToString();
        StubAlejoClient empty = new([]); // a reachable-but-useless response

        await using (SeedTestDbContext context = NewContext(db))
        {
            await new PronounSeeder(context, empty).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using SeedTestDbContext verify = NewContext(db);
        (await verify.Pronouns.CountAsync()).Should().Be(16);
    }

    private sealed class StubAlejoClient : IAlejoPronounClient
    {
        private readonly IReadOnlyList<PronounRecord> _records;

        public StubAlejoClient(IReadOnlyList<PronounRecord> records) => _records = records;

        public Task<IReadOnlyList<PronounRecord>?> FetchAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PronounRecord>?>(_records);

        public Task<AlejoUserPronoun?> LookupUserAsync(
            string twitchLogin,
            CancellationToken ct = default
        ) => Task.FromResult<AlejoUserPronoun?>(null);
    }

    private sealed class OfflineAlejoClient : IAlejoPronounClient
    {
        public Task<IReadOnlyList<PronounRecord>?> FetchAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PronounRecord>?>(null);

        public Task<AlejoUserPronoun?> LookupUserAsync(
            string twitchLogin,
            CancellationToken ct = default
        ) => Task.FromResult<AlejoUserPronoun?>(null);
    }
}
