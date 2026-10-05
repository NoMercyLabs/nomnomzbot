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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Tests.Platform.Templating;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// A streamer sets how TTS says their channel name: Set persists a trimmed value on the channel row, blank
/// clears it, a too-long or control-character value is refused with nothing written, only the named channel
/// changes, and the stored value is what the TTS lexicon then speaks.
/// </summary>
public sealed class ChannelNamePronunciationWriteTests
{
    private static User NewOwner(string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TwitchUserId = name,
            Username = name,
            UsernameNormalized = name.ToLowerInvariant(),
            DisplayName = name,
        };

    private static Channel NewChannel(User owner, string? pronunciation = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = owner.Id,
            Name = owner.Username,
            NameNormalized = owner.UsernameNormalized,
            ExternalChannelId = owner.Username,
            UsernamePronunciation = pronunciation,
        };

    private static async Task<(
        PronounGrammarTestDbContext Db,
        Channel Jd,
        Channel Other
    )> SeedAsync(string? jdPronunciation = null)
    {
        PronounGrammarTestDbContext db = PronounGrammarTestDbContext.New();
        User jdOwner = NewOwner("xX_JD_Xx");
        User otherOwner = NewOwner("plainname");
        Channel jd = NewChannel(jdOwner, jdPronunciation);
        Channel other = NewChannel(otherOwner, "Plain Name");
        db.Users.AddRange(jdOwner, otherOwner);
        db.Channels.AddRange(jd, other);
        await db.SaveChangesAsync();
        return (db, jd, other);
    }

    [Fact]
    public async Task Set_then_Get_round_trips_the_trimmed_value_and_lists_it()
    {
        (PronounGrammarTestDbContext db, Channel jd, _) = await SeedAsync();
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        Result<ChannelNamePronunciationDto> set = await sut.SetAsync(jd.Id, "  Jaydee ");
        Result<ChannelNamePronunciationDto> got = await sut.GetAsync(jd.Id);

        set.IsSuccess.Should().BeTrue();
        set.Value.Should().Be(new ChannelNamePronunciationDto("xX_JD_Xx", "Jaydee"));
        got.Value.Should().Be(new ChannelNamePronunciationDto("xX_JD_Xx", "Jaydee"));
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == jd.Id))
            .UsernamePronunciation.Should()
            .Be("Jaydee");
        (await sut.ListAsync())
            .Should()
            .Contain(new ChannelNamePronunciation("xX_JD_Xx", "Jaydee"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_null_or_blank_clears_the_column(string? blank)
    {
        (PronounGrammarTestDbContext db, Channel jd, _) = await SeedAsync("Jaydee");
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        Result<ChannelNamePronunciationDto> set = await sut.SetAsync(jd.Id, blank);

        set.IsSuccess.Should().BeTrue();
        set.Value.Pronunciation.Should().BeNull();
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == jd.Id))
            .UsernamePronunciation.Should()
            .BeNull();
        (await sut.ListAsync()).Select(n => n.Name).Should().NotContain("xX_JD_Xx");
    }

    [Fact]
    public async Task Set_unknown_channel_is_not_found_and_writes_nothing()
    {
        (PronounGrammarTestDbContext db, _, Channel other) = await SeedAsync();
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        Result<ChannelNamePronunciationDto> set = await sut.SetAsync(Guid.CreateVersion7(), "x");

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == other.Id))
            .UsernamePronunciation.Should()
            .Be("Plain Name");
    }

    [Fact]
    public async Task Set_over_the_limit_is_refused_and_the_row_is_unchanged()
    {
        (PronounGrammarTestDbContext db, Channel jd, _) = await SeedAsync("Jaydee");
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        Result<ChannelNamePronunciationDto> atLimit = await sut.SetAsync(jd.Id, new('a', 100));
        Result<ChannelNamePronunciationDto> over = await sut.SetAsync(jd.Id, new('b', 101));

        atLimit.IsSuccess.Should().BeTrue();
        over.IsFailure.Should().BeTrue();
        over.ErrorCode.Should().Be("VALIDATION_FAILED");
        over.ErrorMessage.Should().Contain("100");
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == jd.Id))
            .UsernamePronunciation.Should()
            .Be(new string('a', 100));
    }

    [Fact]
    public async Task Set_with_a_control_character_is_refused_and_the_row_is_unchanged()
    {
        (PronounGrammarTestDbContext db, Channel jd, _) = await SeedAsync("Jaydee");
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        Result<ChannelNamePronunciationDto> set = await sut.SetAsync(jd.Id, "Jay\ndee");

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == jd.Id))
            .UsernamePronunciation.Should()
            .Be("Jaydee");
    }

    [Fact]
    public async Task Set_changes_only_the_named_channel()
    {
        (PronounGrammarTestDbContext db, Channel jd, Channel other) = await SeedAsync();
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService sut = new(db);

        await sut.SetAsync(jd.Id, "Jaydee");

        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == other.Id))
            .UsernamePronunciation.Should()
            .Be("Plain Name");
    }

    [Fact]
    public async Task The_lexicon_speaks_the_name_the_way_the_streamer_just_set_it()
    {
        (PronounGrammarTestDbContext db, Channel jd, _) = await SeedAsync();
        using PronounGrammarTestDbContext scope = db;
        ChannelNamePronunciationService names = new(db);
        using TtsTestDbContext ttsDb = TtsTestDbContext.New();
        MemoryCacheService cache = new(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance
        );
        TtsLexiconService lexicon = new(ttsDb, cache, names);
        Guid tenant = Guid.CreateVersion7();
        const string Line = "thanks @xX_JD_Xx for the redeem";

        string before = await lexicon.ApplyAsync(tenant, Line);
        await names.SetAsync(jd.Id, "Jaydee");
        string after = await lexicon.ApplyAsync(tenant, Line);

        before.Should().Be(Line);
        after.Should().Be("thanks Jaydee for the redeem");
    }
}
