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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.Tests.Hubs;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The favorite-emotes endpoints return the documented envelope and stay strictly per user: one signed-in
/// user never lists, overwrites or deletes another user's favorites.
/// </summary>
public sealed class FavoriteEmotesControllerTests
{
    private static readonly Guid Alice = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");
    private static readonly Guid Bob = Guid.Parse("0199d000-0000-7000-8000-0000000000c2");

    private static FavoriteEmotesController As(WidgetTestDbContext db, string? userId)
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);
        return new(new FavoriteEmoteService(db), currentUser);
    }

    private static SetFavoriteEmoteRequest Req(string id) =>
        new("SevenTv", id, $"code{id}", $"https://cdn.example/{id}.webp");

    private static T Data<T>(IActionResult result) =>
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<T>>()
            .Which.Data!;

    [Fact]
    public async Task A_put_returns_the_stored_favorite_and_the_next_get_lists_it()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        FavoriteEmotesController controller = As(db, Alice.ToString());

        FavoriteEmoteDto put = Data<FavoriteEmoteDto>(
            await controller.Add(Req("01ABC"), CancellationToken.None)
        );
        IReadOnlyList<FavoriteEmoteDto> listed = Data<IReadOnlyList<FavoriteEmoteDto>>(
            await controller.List(CancellationToken.None)
        );

        put.Provider.Should().Be("SevenTv");
        put.EmoteId.Should().Be("01ABC");
        put.Code.Should().Be("code01ABC");
        put.ImageUrl.Should().Be("https://cdn.example/01ABC.webp");
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(put);
        (await db.FavoriteEmotes.SingleAsync()).UserId.Should().Be(Alice);
    }

    [Fact]
    public async Task One_user_never_lists_another_users_favorites()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        await As(db, Alice.ToString()).Add(Req("1"), CancellationToken.None);
        await As(db, Bob.ToString()).Add(Req("2"), CancellationToken.None);

        IReadOnlyList<FavoriteEmoteDto> alice = Data<IReadOnlyList<FavoriteEmoteDto>>(
            await As(db, Alice.ToString()).List(CancellationToken.None)
        );
        IReadOnlyList<FavoriteEmoteDto> bob = Data<IReadOnlyList<FavoriteEmoteDto>>(
            await As(db, Bob.ToString()).List(CancellationToken.None)
        );

        alice.Select(f => f.EmoteId).Should().Equal("1");
        bob.Select(f => f.EmoteId).Should().Equal("2");
    }

    [Fact]
    public async Task A_delete_removes_only_the_callers_own_row()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        await As(db, Alice.ToString()).Add(Req("1"), CancellationToken.None);
        await As(db, Bob.ToString()).Add(Req("1"), CancellationToken.None);

        IActionResult removed = await As(db, Bob.ToString())
            .Remove("SevenTv", "1", CancellationToken.None);

        removed.Should().BeOfType<OkObjectResult>();
        List<FavoriteEmote> left = await db.FavoriteEmotes.ToListAsync();
        left.Should().ContainSingle().Which.UserId.Should().Be(Alice);
    }

    [Fact]
    public async Task Deleting_another_users_favorite_leaves_it_in_place()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        await As(db, Alice.ToString()).Add(Req("1"), CancellationToken.None);

        await As(db, Bob.ToString()).Remove("SevenTv", "1", CancellationToken.None);

        IReadOnlyList<FavoriteEmoteDto> alice = Data<IReadOnlyList<FavoriteEmoteDto>>(
            await As(db, Alice.ToString()).List(CancellationToken.None)
        );
        alice.Should().ContainSingle().Which.EmoteId.Should().Be("1");
    }

    [Fact]
    public async Task A_caller_without_a_user_id_gets_401_on_every_verb()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        FavoriteEmotesController anonymous = As(db, null);

        (await anonymous.List(CancellationToken.None))
            .Should()
            .BeOfType<UnauthorizedObjectResult>();
        (await anonymous.Add(Req("1"), CancellationToken.None))
            .Should()
            .BeOfType<UnauthorizedObjectResult>();
        (await anonymous.Remove("SevenTv", "1", CancellationToken.None))
            .Should()
            .BeOfType<UnauthorizedObjectResult>();
        (await db.FavoriteEmotes.CountAsync()).Should().Be(0);
    }
}
