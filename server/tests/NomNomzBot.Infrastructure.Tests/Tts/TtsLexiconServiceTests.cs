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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the pronunciation lexicon (tts.md): CRUD persists real rows with the right shape and refuses a
/// duplicate (phrase, match kind); ApplyAsync substitutes word-kind rules case-insensitively at word
/// boundaries only ("JD" never fires inside "JDx"), exact-kind rules as case-sensitive literals, resolves
/// overlaps in one pass without recursing into replacements, stops at the 200-rule bound, serves from the
/// per-channel cache until a write invalidates it, and never crosses tenants.
/// </summary>
public sealed class TtsLexiconServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-3333-7000-8000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019f2a00-3333-7000-8000-000000000002");

    private sealed class Harness
    {
        public required TtsLexiconService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
    }

    private static Harness Build()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        MemoryCacheService cache = new(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance
        );
        return new Harness { Service = new TtsLexiconService(db, cache), Db = db };
    }

    private static UpsertTtsLexiconEntryDto Rule(
        string phrase,
        string replacement,
        string kind = "word"
    ) =>
        new()
        {
            Phrase = phrase,
            Replacement = replacement,
            MatchKind = kind,
        };

    // ── CRUD shape ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsRow_WithTrimmedFieldsAndDefaultKind()
    {
        Harness h = Build();

        Result<TtsLexiconEntryDto> result = await h.Service.CreateAsync(
            Tenant,
            Rule("  JD  ", " Jaydee ")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Phrase.Should().Be("JD");
        result.Value.Replacement.Should().Be("Jaydee");
        result.Value.MatchKind.Should().Be("word");
        result.Value.Id.Should().NotBe(Guid.Empty);

        TtsLexiconEntry row = await h.Db.TtsLexiconEntries.SingleAsync();
        row.BroadcasterId.Should().Be(Tenant);
        row.Phrase.Should().Be("JD");
        row.Replacement.Should().Be("Jaydee");
        row.MatchKind.Should().Be("word");
        row.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhraseAndKind_IsRefused_ButOtherKindIsAllowed()
    {
        Harness h = Build();
        (await h.Service.CreateAsync(Tenant, Rule("brb", "be right back")))
            .IsSuccess.Should()
            .BeTrue();

        Result<TtsLexiconEntryDto> duplicate = await h.Service.CreateAsync(
            Tenant,
            Rule("brb", "bathroom break")
        );
        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("ALREADY_EXISTS");

        // Same phrase under the OTHER matcher is a different rule — allowed.
        Result<TtsLexiconEntryDto> exactKind = await h.Service.CreateAsync(
            Tenant,
            Rule("brb", "be right back", "exact")
        );
        exactKind.IsSuccess.Should().BeTrue();

        (await h.Db.TtsLexiconEntries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_InvalidInput_IsRejected()
    {
        Harness h = Build();

        (await h.Service.CreateAsync(Tenant, Rule("  ", "x")))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        (await h.Service.CreateAsync(Tenant, Rule("x", " ")))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        (await h.Service.CreateAsync(Tenant, Rule("x", "y", "fuzzy")))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        (await h.Db.TtsLexiconEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_RewritesTheRow_AndRefusesCollidingWithAnotherRule()
    {
        Harness h = Build();
        Guid first = (await h.Service.CreateAsync(Tenant, Rule("brb", "be right back"))).Value.Id;
        Guid second = (await h.Service.CreateAsync(Tenant, Rule("afk", "away"))).Value.Id;

        Result<TtsLexiconEntryDto> updated = await h.Service.UpdateAsync(
            Tenant,
            second,
            Rule("afk", "away from keyboard", "exact")
        );
        updated.IsSuccess.Should().BeTrue();

        TtsLexiconEntry row = await h.Db.TtsLexiconEntries.SingleAsync(e => e.Id == second);
        row.Replacement.Should().Be("away from keyboard");
        row.MatchKind.Should().Be("exact");

        // Renaming it onto the FIRST rule's (phrase, kind) collides.
        Result<TtsLexiconEntryDto> collision = await h.Service.UpdateAsync(
            Tenant,
            second,
            Rule("brb", "whatever")
        );
        collision.ErrorCode.Should().Be("ALREADY_EXISTS");
        first.Should().NotBe(second);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_AndFreesTheSlotForReAdd()
    {
        Harness h = Build();
        Guid id = (await h.Service.CreateAsync(Tenant, Rule("brb", "be right back"))).Value.Id;

        (await h.Service.DeleteAsync(Tenant, id)).IsSuccess.Should().BeTrue();

        // Soft-deleted, not physically gone; the InMemory fake has no global filter, so read it raw.
        TtsLexiconEntry row = await h.Db.TtsLexiconEntries.IgnoreQueryFilters().SingleAsync();
        row.DeletedAt.Should().NotBeNull();

        (await h.Service.DeleteAsync(Tenant, id)).ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheChannelsRules_OrderedByPhrase()
    {
        Harness h = Build();
        await h.Service.CreateAsync(Tenant, Rule("zulu", "z"));
        await h.Service.CreateAsync(Tenant, Rule("alpha", "a"));
        await h.Service.CreateAsync(OtherTenant, Rule("foreign", "f"));

        Result<IReadOnlyList<TtsLexiconEntryDto>> result = await h.Service.ListAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(e => e.Phrase).Should().Equal("alpha", "zulu");
    }

    // ── ApplyAsync — the dispatch hot path ───────────────────────────────────

    [Fact]
    public async Task ApplyAsync_WordKind_MatchesWholeWordsCaseInsensitively_NotInsideWords()
    {
        Harness h = Build();
        await h.Service.CreateAsync(Tenant, Rule("JD", "Jaydee"));

        (await h.Service.ApplyAsync(Tenant, "jd says hi to JD!"))
            .Should()
            .Be("Jaydee says hi to Jaydee!");
        // "JD" inside a longer word never fires.
        (await h.Service.ApplyAsync(Tenant, "JDx and xJD stay put"))
            .Should()
            .Be("JDx and xJD stay put");
    }

    [Fact]
    public async Task ApplyAsync_WordKind_BindsCorrectlyOnPunctuationEdges()
    {
        Harness h = Build();
        await h.Service.CreateAsync(Tenant, Rule("<3", "heart"));

        (await h.Service.ApplyAsync(Tenant, "sending <3 to chat"))
            .Should()
            .Be("sending heart to chat");
    }

    [Fact]
    public async Task ApplyAsync_ExactKind_IsCaseSensitiveLiteral()
    {
        Harness h = Build();
        await h.Service.CreateAsync(Tenant, Rule("USA", "you ess ay", "exact"));

        (await h.Service.ApplyAsync(Tenant, "USA vs usa")).Should().Be("you ess ay vs usa");
    }

    [Fact]
    public async Task ApplyAsync_SinglePass_ReplacementsAreNeverReMatched()
    {
        Harness h = Build();
        // A recursive/multi-pass implementation would turn "aa" into "aab" and then match the inserted "a"s again.
        await h.Service.CreateAsync(Tenant, Rule("aa", "aab", "exact"));
        await h.Service.CreateAsync(Tenant, Rule("aab", "boom", "exact"));

        // "aa" and "aab" both match at index 0 — the longer wins; its output is NOT re-processed.
        (await h.Service.ApplyAsync(Tenant, "aab"))
            .Should()
            .Be("boom");
        (await h.Service.ApplyAsync(Tenant, "aa")).Should().Be("aab");
    }

    [Fact]
    public async Task ApplyAsync_AppliesAtMost200Entries()
    {
        Harness h = Build();
        // 200 filler rules that occupy the bound (ordered by phrase: "k000…k199" sort before "zz"),
        // plus one rule beyond it that must NOT be applied.
        for (int i = 0; i < TtsLexiconService.MaxAppliedEntries; i++)
        {
            h.Db.TtsLexiconEntries.Add(
                new TtsLexiconEntry
                {
                    BroadcasterId = Tenant,
                    Phrase = $"k{i:000}",
                    Replacement = "filler",
                    MatchKind = "word",
                }
            );
        }
        h.Db.TtsLexiconEntries.Add(
            new TtsLexiconEntry
            {
                BroadcasterId = Tenant,
                Phrase = "zz",
                Replacement = "beyond the bound",
                MatchKind = "word",
            }
        );
        await h.Db.SaveChangesAsync();

        (await h.Service.ApplyAsync(Tenant, "k000 zz k199")).Should().Be("filler zz filler");
    }

    [Fact]
    public async Task ApplyAsync_WriteInvalidatesTheCache_SoTheNextApplyReflectsIt()
    {
        Harness h = Build();
        await h.Service.CreateAsync(Tenant, Rule("brb", "be right back"));

        // Prime the cache.
        (await h.Service.ApplyAsync(Tenant, "brb chat"))
            .Should()
            .Be("be right back chat");

        // A write must evict the cached rule set — the very next apply sees the new rule.
        await h.Service.CreateAsync(Tenant, Rule("gg", "good game"));
        (await h.Service.ApplyAsync(Tenant, "gg brb")).Should().Be("good game be right back");

        // Deleting a rule is reflected too.
        Guid brb = (await h.Service.ListAsync(Tenant)).Value.Single(e => e.Phrase == "brb").Id;
        await h.Service.DeleteAsync(Tenant, brb);
        (await h.Service.ApplyAsync(Tenant, "gg brb")).Should().Be("good game brb");
    }

    [Fact]
    public async Task ApplyAsync_IsTenantIsolated()
    {
        Harness h = Build();
        await h.Service.CreateAsync(OtherTenant, Rule("brb", "be right back"));

        (await h.Service.ApplyAsync(Tenant, "brb chat")).Should().Be("brb chat");
    }
}
