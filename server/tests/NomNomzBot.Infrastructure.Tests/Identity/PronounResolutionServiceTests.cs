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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the lazy, cache-gated pronoun resolution (spec D3) that GAP E3-1 wires into chat ingest:
/// the provider is hit at most once per viewer per cooldown window, a manual-override viewer is never
/// touched, and a successful resolution writes <c>User.PronounId</c>/<c>AltPronounId</c> back to the row.
/// Runs the real <see cref="AppDbContext"/> on SQLite in-memory (same provider the self-host runtime uses)
/// rather than a hand-rolled fake, since <see cref="PronounResolutionService"/> depends on the concrete
/// context, not the <c>IApplicationDbContext</c> abstraction.
/// </summary>
public sealed class PronounResolutionServiceTests
{
    [Fact]
    public async Task First_call_resolves_from_the_provider_and_writes_the_pronoun()
    {
        await using Harness harness = await Harness.CreateAsync();
        Guid userId = await harness.SeedUserAsync("stoney_eagle", manualOverride: false);
        await harness.SeedPronounAsync("theythem", "they/them", "they", "them");
        await harness.SeedPronounAsync("sheher", "she/her", "she", "her");

        CountingProvider provider = new(new ResolvedPronounRef("theythem", "sheher"));
        PronounResolutionService service = harness.NewService(provider);

        await service.ResolveAndApplyAsync(userId, "stoney_eagle");

        provider.CallCount.Should().Be(1);

        await using AppDbContext verify = harness.NewDbContext();
        User user = await verify
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .SingleAsync(u => u.Id == userId);
        user.Pronoun.Should().NotBeNull();
        user.Pronoun!.Key.Should().Be("theythem");
        user.AltPronoun.Should().NotBeNull();
        user.AltPronoun!.Key.Should().Be("sheher");
    }

    [Fact]
    public async Task Second_call_within_the_cooldown_does_not_hit_the_provider_again()
    {
        await using Harness harness = await Harness.CreateAsync();
        Guid userId = await harness.SeedUserAsync("stoney_eagle", manualOverride: false);
        await harness.SeedPronounAsync("theythem", "they/them", "they", "them");

        CountingProvider provider = new(new ResolvedPronounRef("theythem", null));

        // Two separate PronounResolutionService instances (fresh DbContext each — mirrors two separate
        // chat messages, each dispatched in its own EventBus scope) sharing the SAME IMemoryCache
        // singleton, exactly like production DI. The cache-gate lives in the cache, not the DbContext.
        await harness.NewService(provider).ResolveAndApplyAsync(userId, "stoney_eagle");
        await harness.NewService(provider).ResolveAndApplyAsync(userId, "stoney_eagle");

        provider
            .CallCount.Should()
            .Be(1, "the 24h cache gate must collapse the second call to a no-op");
    }

    [Fact]
    public async Task Manual_override_viewer_is_never_touched()
    {
        await using Harness harness = await Harness.CreateAsync();
        Guid userId = await harness.SeedUserAsync("stoney_eagle", manualOverride: true);
        await harness.SeedPronounAsync("theythem", "they/them", "they", "them");

        CountingProvider provider = new(new ResolvedPronounRef("theythem", null));
        PronounResolutionService service = harness.NewService(provider);

        await service.ResolveAndApplyAsync(userId, "stoney_eagle");

        provider
            .CallCount.Should()
            .Be(0, "a manual override must never be clobbered by auto-resolution");

        await using AppDbContext verify = harness.NewDbContext();
        User user = await verify.Users.SingleAsync(u => u.Id == userId);
        user.PronounId.Should().BeNull();
    }

    private sealed class CountingProvider(ResolvedPronounRef? result) : IPronounProvider
    {
        public int CallCount { get; private set; }

        public string Name => "test-provider";

        public Task<IReadOnlyDictionary<string, PronounCatalogEntry>?> GetCatalogAsync(
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyDictionary<string, PronounCatalogEntry>?>(null);

        public Task<ResolvedPronounRef?> LookupAsync(
            string twitchLogin,
            CancellationToken ct = default
        )
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    // ─── Harness ────────────────────────────────────────────────────────────────

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly IMemoryCache _cache;

        private Harness(SqliteConnection connection, IMemoryCache cache)
        {
            _connection = connection;
            _cache = cache;
        }

        public static async Task<Harness> CreateAsync()
        {
            SqliteConnection connection = new("DataSource=:memory:");
            await connection.OpenAsync();

            DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            await using (AppDbContext seedContext = new(options))
                await seedContext.Database.EnsureCreatedAsync();

            return new Harness(connection, new MemoryCache(new MemoryCacheOptions()));
        }

        public AppDbContext NewDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        public PronounResolutionService NewService(IPronounProvider provider) =>
            new(NewDbContext(), provider, _cache, NullLogger<PronounResolutionService>.Instance);

        public async Task<Guid> SeedUserAsync(string login, bool manualOverride)
        {
            Guid id = Guid.CreateVersion7();
            await using AppDbContext db = NewDbContext();
            db.Users.Add(
                new User
                {
                    Id = id,
                    TwitchUserId = Guid.NewGuid().ToString("N"),
                    Username = login,
                    UsernameNormalized = login.ToLowerInvariant(),
                    DisplayName = login,
                    PronounManualOverride = manualOverride,
                }
            );
            await db.SaveChangesAsync();
            return id;
        }

        public async Task SeedPronounAsync(string key, string name, string subject, string @object)
        {
            await using AppDbContext db = NewDbContext();
            db.Pronouns.Add(
                new Pronoun
                {
                    Key = key,
                    Name = name,
                    Subject = subject,
                    Object = @object,
                    // Grammar columns are irrelevant to this test (pronoun resolution/linking only) —
                    // any non-null values satisfy the NOT NULL constraint.
                    Possessive = subject,
                    GenderedTerm = "person",
                    Singular = false,
                }
            );
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            (_cache as IDisposable)?.Dispose();
            await _connection.DisposeAsync();
        }
    }
}
