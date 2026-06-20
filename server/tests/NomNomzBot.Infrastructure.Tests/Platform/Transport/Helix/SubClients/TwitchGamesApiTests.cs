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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// Behavioural tests for the Games sub-client: each method builds the exact Helix request (verb / path /
/// App auth / query) from the supplied identifiers or page, and maps the response. No tenant resolution or
/// scope gating is involved — these are public App-token reads — so the capturing transport alone proves the
/// request shape (including repeated id / name / igdb_id params) and the DTO mapping with no HTTP.
/// </summary>
public class TwitchGamesApiTests
{
    [Fact]
    public async Task GetGames_RepeatsIdNameIgdbParams_AppToken_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult =
                (IReadOnlyList<TwitchGame>)
                    [
                        new TwitchGame(
                            "509658",
                            "Just Chatting",
                            "https://box-art/{width}x{height}.jpg",
                            "417"
                        ),
                    ],
        };
        TwitchGamesApi api = new(transport);

        Result<IReadOnlyList<TwitchGame>> result = await api.GetGamesAsync(
            ["509658", "21779"],
            ["Just Chatting"],
            ["417"]
        );

        result.IsSuccess.Should().BeTrue();
        TwitchGame game = result.Value.Should().ContainSingle().Subject;
        game.Id.Should().Be("509658");
        game.Name.Should().Be("Just Chatting");
        game.BoxArtUrl.Should().Be("https://box-art/{width}x{height}.jpg");
        game.IgdbId.Should().Be("417");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("games");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport
            .LastRequest.Query.Should()
            .ContainInOrder(
                new KeyValuePair<string, string>("id", "509658"),
                new KeyValuePair<string, string>("id", "21779")
            );
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "name" && q.Value == "Just Chatting");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "igdb_id" && q.Value == "417");
    }

    [Fact]
    public async Task GetGames_NullCollections_SendEmptyQuery()
    {
        CapturingHelixTransport transport = new() { ListResult = (IReadOnlyList<TwitchGame>)[] };
        TwitchGamesApi api = new(transport);

        await api.GetGamesAsync(null, null, null);

        transport.LastRequest!.Path.Should().Be("games");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGames_OnlyNames_OmitsIdAndIgdb()
    {
        CapturingHelixTransport transport = new() { ListResult = (IReadOnlyList<TwitchGame>)[] };
        TwitchGamesApi api = new(transport);

        await api.GetGamesAsync(null, ["Fortnite", "Minecraft"], null);

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "id");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "igdb_id");
        transport
            .LastRequest.Query.Should()
            .ContainInOrder(
                new KeyValuePair<string, string>("name", "Fortnite"),
                new KeyValuePair<string, string>("name", "Minecraft")
            );
    }

    [Fact]
    public async Task GetTopGames_BuildsAppTokenPagedQuery_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchGame>(
                [
                    new TwitchGame(
                        "33214",
                        "Fortnite",
                        "https://box-art/fortnite-{width}x{height}.jpg",
                        "1905"
                    ),
                ],
                "next",
                1
            ),
        };
        TwitchGamesApi api = new(transport);

        Result<TwitchPage<TwitchGame>> result = await api.GetTopGamesAsync(
            new TwitchPageRequest(After: "cur", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("next");
        TwitchGame game = result.Value.Items.Should().ContainSingle().Subject;
        game.Id.Should().Be("33214");
        game.Name.Should().Be("Fortnite");
        game.BoxArtUrl.Should().Be("https://box-art/fortnite-{width}x{height}.jpg");
        game.IgdbId.Should().Be("1905");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("games/top");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
    }

    [Fact]
    public async Task GetTopGames_WithoutCursor_OmitsAfter()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchGame>([], null, 0),
        };
        TwitchGamesApi api = new(transport);

        await api.GetTopGamesAsync(new TwitchPageRequest(PageSize: 100));

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "after");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "100");
    }
}
