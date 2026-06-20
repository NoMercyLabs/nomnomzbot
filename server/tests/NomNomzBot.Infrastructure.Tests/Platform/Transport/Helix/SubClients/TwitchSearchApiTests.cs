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
/// Behavioural tests for the Search sub-client: each method builds the exact Helix request (verb / path /
/// App auth / query) from the free-text query and page, and maps the paged response. No tenant resolution or
/// scope gating is involved — these are public App-token reads — so the capturing transport alone proves the
/// request shape and the DTO mapping with no HTTP.
/// </summary>
public class TwitchSearchApiTests
{
    [Fact]
    public async Task SearchCategories_BuildsAppTokenPagedQuery_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchSearchCategory>(
                [
                    new TwitchSearchCategory(
                        "509658",
                        "Just Chatting",
                        "https://box-art/{width}x{height}.jpg"
                    ),
                ],
                "next",
                1
            ),
        };
        TwitchSearchApi api = new(transport);

        Result<TwitchPage<TwitchSearchCategory>> result = await api.SearchCategoriesAsync(
            "chatting",
            new TwitchPageRequest(After: "cur", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("next");
        result.Value.Items.Should().ContainSingle();
        TwitchSearchCategory category = result.Value.Items[0];
        category.Id.Should().Be("509658");
        category.Name.Should().Be("Just Chatting");
        category.BoxArtUrl.Should().Be("https://box-art/{width}x{height}.jpg");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("search/categories");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "query" && q.Value == "chatting");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
    }

    [Fact]
    public async Task SearchCategories_WithoutCursor_OmitsAfter()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchSearchCategory>([], null, 0),
        };
        TwitchSearchApi api = new(transport);

        await api.SearchCategoriesAsync("art", new TwitchPageRequest(PageSize: 100));

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "after");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "100");
    }

    [Fact]
    public async Task SearchChannels_BuildsAppTokenPagedQuery_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchSearchChannel>(
                [
                    new TwitchSearchChannel(
                        "44322889",
                        "dallas",
                        "Dallas",
                        "en",
                        "509658",
                        "Just Chatting",
                        true,
                        ["English", "Coding"],
                        ["tag-1"],
                        "https://thumb/dallas.jpg",
                        "Building a bot",
                        DateTimeOffset.UnixEpoch
                    ),
                ],
                "next",
                1
            ),
        };
        TwitchSearchApi api = new(transport);

        Result<TwitchPage<TwitchSearchChannel>> result = await api.SearchChannelsAsync(
            "dallas",
            true,
            new TwitchPageRequest(After: "cur", PageSize: 40)
        );

        result.IsSuccess.Should().BeTrue();
        TwitchSearchChannel channel = result.Value.Items.Should().ContainSingle().Subject;
        channel.Id.Should().Be("44322889");
        channel.BroadcasterLogin.Should().Be("dallas");
        channel.DisplayName.Should().Be("Dallas");
        channel.GameName.Should().Be("Just Chatting");
        channel.IsLive.Should().BeTrue();
        channel.Tags.Should().BeEquivalentTo("English", "Coding");
        channel.TagIds.Should().ContainSingle().Which.Should().Be("tag-1");
        channel.StartedAt.Should().Be(DateTimeOffset.UnixEpoch);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("search/channels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport.LastRequest.Query.Should().Contain(q => q.Key == "query" && q.Value == "dallas");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "40");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "live_only" && q.Value == "true");
    }

    [Fact]
    public async Task SearchChannels_LiveOnlyFalse_SendsFalse()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchSearchChannel>([], null, 0),
        };
        TwitchSearchApi api = new(transport);

        await api.SearchChannelsAsync("dallas", false, new TwitchPageRequest(PageSize: 100));

        transport
            .LastRequest!.Query.Should()
            .Contain(q => q.Key == "live_only" && q.Value == "false");
    }

    [Fact]
    public async Task SearchChannels_NullLiveOnly_OmitsLiveOnly()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchSearchChannel>([], null, 0),
        };
        TwitchSearchApi api = new(transport);

        await api.SearchChannelsAsync("dallas", null, new TwitchPageRequest(PageSize: 100));

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "live_only");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "after");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "query" && q.Value == "dallas");
    }
}
