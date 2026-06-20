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
/// Behavioural tests for the Chat-assets sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope where one applies, and builds the exact Helix request (verb / path / auth /
/// query). The capturing transport lets us assert the request shape, the short-circuit paths and the nested
/// DTO mapping (emote images, badge versions, shared-session participants) with no HTTP.
/// </summary>
public class TwitchChatAssetsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "198704263";

    private static TwitchChatAssetsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetChatters_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatAssetsApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchChatter>> result = await api.GetChattersAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetChatters_WithScope_BuildsUserTokenPagedQuery_WithModeratorId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchChatter>(
                [new TwitchChatter("123", "viewer", "Viewer")],
                "cursor",
                42
            ),
        };
        TwitchChatAssetsApi api = Build(transport, TwitchScopes.ModeratorReadChatters);

        Result<TwitchPage<TwitchChatter>> result = await api.GetChattersAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(42);
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle().Which.UserName.Should().Be("Viewer");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/chatters");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetChannelEmotes_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatAssetsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<IReadOnlyList<TwitchChannelEmote>> result = await api.GetChannelEmotesAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetChannelEmotes_BuildsAppTokenRequest_MapsNestedImages()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChannelEmote>
            {
                new(
                    "301",
                    "modCheck",
                    new TwitchEmoteImages("u1", "u2", "u4"),
                    "1000",
                    "subscriptions",
                    "set-1",
                    ["static", "animated"],
                    ["1.0", "2.0", "3.0"],
                    ["light", "dark"]
                ),
            },
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChannelEmote>> result = await api.GetChannelEmotesAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        TwitchChannelEmote emote = result.Value.Should().ContainSingle().Subject;
        emote.Name.Should().Be("modCheck");
        emote.EmoteType.Should().Be("subscriptions");
        emote.EmoteSetId.Should().Be("set-1");
        emote.Images.Url2x.Should().Be("u2");
        emote.Format.Should().Contain("animated");
        emote.ThemeMode.Should().BeEquivalentTo("light", "dark");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/emotes");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetGlobalEmotes_BuildsAppTokenRequest_NoParams()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchGlobalEmote>
            {
                new(
                    "555",
                    "Kappa",
                    new TwitchEmoteImages("g1", "g2", "g4"),
                    ["static"],
                    ["1.0"],
                    ["light"]
                ),
            },
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<IReadOnlyList<TwitchGlobalEmote>> result = await api.GetGlobalEmotesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Name.Should().Be("Kappa");
        transport.LastRequest!.Path.Should().Be("chat/emotes/global");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().BeNull();
    }

    [Fact]
    public async Task GetEmoteSets_RepeatsEmoteSetIdQuery_PerId()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchEmoteSetEmote>
            {
                new(
                    "9",
                    "PogChamp",
                    new TwitchEmoteImages("s1", "s2", "s4"),
                    "subscriptions",
                    "301",
                    "owner-1",
                    ["static"],
                    ["1.0"],
                    ["light"]
                ),
            },
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<IReadOnlyList<TwitchEmoteSetEmote>> result = await api.GetEmoteSetsAsync([
            "301",
            "302",
        ]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.OwnerId.Should().Be("owner-1");
        transport.LastRequest!.Path.Should().Be("chat/emotes/set");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .HaveCount(2)
            .And.OnlyContain(q => q.Key == "emote_set_id");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "301");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "302");
    }

    [Fact]
    public async Task GetUserEmotes_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatAssetsApi api = Build(transport);

        Result<TwitchPage<TwitchUserEmote>> result = await api.GetUserEmotesAsync(Tenant, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUserEmotes_WithScope_BuildsUserTokenQuery_WithUserIdAndCursor()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchUserEmote>(
                [
                    new TwitchUserEmote(
                        "777",
                        "prime",
                        "prime",
                        "set-x",
                        "owner-x",
                        ["static"],
                        ["1.0"],
                        ["light"]
                    ),
                ],
                "next",
                0
            ),
        };
        TwitchChatAssetsApi api = Build(transport, TwitchScopes.UserReadEmotes);

        Result<TwitchPage<TwitchUserEmote>> result = await api.GetUserEmotesAsync(Tenant, "cur");

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("next");
        result.Value.Items.Should().ContainSingle().Which.EmoteType.Should().Be("prime");
        transport.LastRequest!.Path.Should().Be("chat/emotes/user");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
    }

    [Fact]
    public async Task GetUserEmotes_NoCursor_OmitsAfterParam()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchUserEmote>([], null, 0),
        };
        TwitchChatAssetsApi api = Build(transport, TwitchScopes.UserReadEmotes);

        await api.GetUserEmotesAsync(Tenant, null);

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "after");
    }

    [Fact]
    public async Task GetChannelChatBadges_BuildsAppTokenRequest_MapsNestedVersions()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChatBadgeSet>
            {
                new(
                    "subscriber",
                    [
                        new TwitchChatBadgeVersion(
                            "0",
                            "b1",
                            "b2",
                            "b4",
                            "Subscriber",
                            "Subscriber badge",
                            "visit_url",
                            "https://example.test"
                        ),
                    ]
                ),
            },
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChatBadgeSet>> result = await api.GetChannelChatBadgesAsync(
            Tenant
        );

        result.IsSuccess.Should().BeTrue();
        TwitchChatBadgeSet set = result.Value.Should().ContainSingle().Subject;
        set.SetId.Should().Be("subscriber");
        TwitchChatBadgeVersion version = set.Versions.Should().ContainSingle().Subject;
        version.Id.Should().Be("0");
        version.ImageUrl4x.Should().Be("b4");
        version.Title.Should().Be("Subscriber");
        version.ClickAction.Should().Be("visit_url");
        version.ClickUrl.Should().Be("https://example.test");
        transport.LastRequest!.Path.Should().Be("chat/badges");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetGlobalChatBadges_BuildsAppTokenRequest_NoParams()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChatBadgeSet>
            {
                new(
                    "admin",
                    [new TwitchChatBadgeVersion("1", "a1", "a2", "a4", "Admin", "", "", "")]
                ),
            },
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChatBadgeSet>> result = await api.GetGlobalChatBadgesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.SetId.Should().Be("admin");
        transport.LastRequest!.Path.Should().Be("chat/badges/global");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().BeNull();
    }

    [Fact]
    public async Task GetSharedChatSession_BuildsAppTokenRequest_MapsParticipants()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchSharedChatSession(
                "359bce59-fa4e-41a5-bd6f-9bc0c8360485",
                TwitchId,
                [
                    new TwitchSharedChatParticipant(TwitchId),
                    new TwitchSharedChatParticipant("487263401"),
                ],
                DateTimeOffset.Parse("2024-09-29T19:45:37Z"),
                DateTimeOffset.Parse("2024-09-29T19:45:37Z")
            ),
        };
        TwitchChatAssetsApi api = Build(transport);

        Result<TwitchSharedChatSession> result = await api.GetSharedChatSessionAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be("359bce59-fa4e-41a5-bd6f-9bc0c8360485");
        result.Value.HostBroadcasterId.Should().Be(TwitchId);
        result
            .Value.Participants.Should()
            .HaveCount(2)
            .And.Contain(p => p.BroadcasterId == "487263401");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/shared_chat_session");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetSharedChatSession_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatAssetsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchSharedChatSession> result = await api.GetSharedChatSessionAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }
}
