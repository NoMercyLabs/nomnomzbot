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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the per-user favorite emote store: an add lands with every field and lists newest first, a repeat
/// add keeps one row, a delete removes only its own row, another user's rows are invisible and untouchable,
/// and the 201st favorite is refused with a clear message. Runs on a real relational SQLite database.
/// </summary>
public sealed class FavoriteEmoteServiceTests
{
    private static readonly Guid Alice = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");
    private static readonly Guid Bob = Guid.Parse("0199d000-0000-7000-8000-0000000000c2");

    private static (FavoriteEmoteService Sut, BanLedgerTestDbContext Db) Build()
    {
        BanLedgerTestDbContext db = BanLedgerTestDbContext.New();
        return (new(db), db);
    }

    private static SetFavoriteEmoteRequest Req(string id, string provider = "SevenTv") =>
        new(provider, id, $"code{id}", $"https://cdn.example/{id}.webp");

    [Fact]
    public async Task Add_then_list_returns_the_row_with_every_field()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();

        Result<FavoriteEmoteDto> added = await sut.AddAsync(
            Alice,
            new("SevenTv", "01ABC", "Pog", "https://cdn.example/pog.webp")
        );
        Result<IReadOnlyList<FavoriteEmoteDto>> listed = await sut.ListAsync(Alice);

        added.IsSuccess.Should().BeTrue();
        listed.Value.Should().ContainSingle();
        FavoriteEmoteDto row = listed.Value[0];
        row.Provider.Should().Be("SevenTv");
        row.EmoteId.Should().Be("01ABC");
        row.Code.Should().Be("Pog");
        row.ImageUrl.Should().Be("https://cdn.example/pog.webp");
        row.CreatedAt.Should().NotBe(default);
        (await db.FavoriteEmotes.SingleAsync()).UserId.Should().Be(Alice);
    }

    [Fact]
    public async Task List_is_newest_first()
    {
        (FavoriteEmoteService sut, _) = Build();
        await sut.AddAsync(Alice, Req("1"));
        await Task.Delay(15);
        await sut.AddAsync(Alice, Req("2"));
        await Task.Delay(15);
        await sut.AddAsync(Alice, Req("3"));

        Result<IReadOnlyList<FavoriteEmoteDto>> listed = await sut.ListAsync(Alice);

        listed.Value.Select(f => f.EmoteId).Should().Equal("3", "2", "1");
    }

    [Fact]
    public async Task A_second_add_of_the_same_emote_does_not_duplicate_and_refreshes_the_snapshot()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        await sut.AddAsync(Alice, new("Bttv", "9", "OldCode", null));

        Result<FavoriteEmoteDto> again = await sut.AddAsync(
            Alice,
            new("Bttv", "9", "NewCode", "https://cdn.example/9.png")
        );

        again.IsSuccess.Should().BeTrue();
        FavoriteEmote row = await db.FavoriteEmotes.SingleAsync();
        row.Code.Should().Be("NewCode");
        row.ImageUrl.Should().Be("https://cdn.example/9.png");
    }

    [Fact]
    public async Task The_same_emote_id_under_another_provider_is_a_separate_favorite()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        await sut.AddAsync(Alice, Req("5", "Bttv"));
        await sut.AddAsync(Alice, Req("5", "Ffz"));

        (await db.FavoriteEmotes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Remove_deletes_only_that_row()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        await sut.AddAsync(Alice, Req("1"));
        await sut.AddAsync(Alice, Req("2"));
        await sut.AddAsync(Alice, Req("1", "Bttv"));

        Result removed = await sut.RemoveAsync(Alice, "SevenTv", "1");

        removed.IsSuccess.Should().BeTrue();
        List<FavoriteEmote> left = await db.FavoriteEmotes.OrderBy(f => f.Code).ToListAsync();
        left.Select(f => (f.Provider, f.EmoteId))
            .Should()
            .BeEquivalentTo([("SevenTv", "2"), ("Bttv", "1")]);
    }

    [Fact]
    public async Task Removing_a_missing_favorite_succeeds()
    {
        (FavoriteEmoteService sut, _) = Build();

        (await sut.RemoveAsync(Alice, "SevenTv", "nope")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Another_user_never_sees_or_deletes_your_rows()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        await sut.AddAsync(Alice, Req("1"));

        (await sut.ListAsync(Bob)).Value.Should().BeEmpty();
        await sut.RemoveAsync(Bob, "SevenTv", "1");

        (await sut.ListAsync(Alice)).Value.Should().ContainSingle();
        (await db.FavoriteEmotes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Two_users_may_favorite_the_same_emote()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        await sut.AddAsync(Alice, Req("1"));

        (await sut.AddAsync(Bob, Req("1"))).IsSuccess.Should().BeTrue();

        (await db.FavoriteEmotes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task The_201st_favorite_fails_with_the_cap_message_and_stores_nothing()
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();
        for (int i = 0; i < FavoriteEmoteLimits.MaxPerUser; i++)
            (await sut.AddAsync(Alice, Req(i.ToString()))).IsSuccess.Should().BeTrue();

        Result<FavoriteEmoteDto> over = await sut.AddAsync(Alice, Req("one-too-many"));

        over.IsFailure.Should().BeTrue();
        over.ErrorCode.Should().Be("VALIDATION_FAILED");
        over.ErrorMessage.Should().Contain("200");
        (await db.FavoriteEmotes.CountAsync()).Should().Be(200);
    }

    [Fact]
    public async Task Re_adding_an_existing_favorite_at_the_cap_still_succeeds()
    {
        (FavoriteEmoteService sut, _) = Build();
        for (int i = 0; i < FavoriteEmoteLimits.MaxPerUser; i++)
            await sut.AddAsync(Alice, Req(i.ToString()));

        (await sut.AddAsync(Alice, Req("7"))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task The_cap_is_per_user()
    {
        (FavoriteEmoteService sut, _) = Build();
        for (int i = 0; i < FavoriteEmoteLimits.MaxPerUser; i++)
            await sut.AddAsync(Alice, Req(i.ToString()));

        (await sut.AddAsync(Bob, Req("0"))).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("Mastodon", "1", "x")]
    [InlineData("SevenTv", "", "x")]
    [InlineData("SevenTv", "1", "")]
    public async Task An_unknown_provider_or_a_blank_id_or_code_is_a_validation_error(
        string provider,
        string emoteId,
        string code
    )
    {
        (FavoriteEmoteService sut, BanLedgerTestDbContext db) = Build();

        Result<FavoriteEmoteDto> result = await sut.AddAsync(
            Alice,
            new(provider, emoteId, code, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.FavoriteEmotes.CountAsync()).Should().Be(0);
    }
}
