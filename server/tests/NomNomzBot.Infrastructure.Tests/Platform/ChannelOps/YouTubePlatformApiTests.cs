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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NomNomzBot.Infrastructure.Platform.ChannelOps;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.ChannelOps;

/// <summary>
/// Proves the YouTube half of the channel-ops seam: a title change while live rides the PRIMARY channel's
/// token into <c>liveBroadcasts.update</c>; offline is an honest <c>NOT_FOUND</c> with no API call;
/// fields YouTube cannot represent (category/tags) are REJECTED, never silently dropped alongside an
/// applied title.
/// </summary>
public sealed class YouTubePlatformApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid Primary = Guid.Parse("0192b000-0000-7000-8000-0000000000f2");

    private static (
        YouTubePlatformApi Api,
        YouTubeLiveChatSessionRegistry Sessions,
        IYouTubeLiveChatClient Client
    ) Build(string? token = "bearer-1")
    {
        YouTubeLiveChatSessionRegistry sessions = new();
        IYouTubeAccessTokenProvider tokens = Substitute.For<IYouTubeAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Primary, Arg.Any<CancellationToken>()).Returns(token);
        IYouTubeLiveChatClient client = Substitute.For<IYouTubeLiveChatClient>();
        client
            .UpdateActiveBroadcastTitleAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(call.ArgAt<string>(1)));

        return (new YouTubePlatformApi(sessions, tokens, client), sessions, client);
    }

    [Fact]
    public async Task A_title_change_while_live_rides_the_primary_token()
    {
        (
            YouTubePlatformApi api,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new PlatformStreamInfoUpdate(Title: "fresh title")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("fresh title");
        await client
            .Received(1)
            .UpdateActiveBroadcastTitleAsync(
                "bearer-1",
                "fresh title",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Offline_is_an_honest_not_found_with_no_api_call()
    {
        (YouTubePlatformApi api, _, IYouTubeLiveChatClient client) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new PlatformStreamInfoUpdate(Title: "t")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await client
            .DidNotReceiveWithAnyArgs()
            .UpdateActiveBroadcastTitleAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unrepresentable_fields_are_rejected_not_silently_dropped()
    {
        // A title + category request must NOT half-apply the title and drop the category — the caller
        // would believe both landed.
        (
            YouTubePlatformApi api,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");

        Result<PlatformStreamInfoApplied> withCategory = await api.UpdateStreamInfoAsync(
            Tenant,
            new PlatformStreamInfoUpdate(Title: "t", CategoryName: "Just Chatting")
        );
        Result<PlatformStreamInfoApplied> withTags = await api.UpdateStreamInfoAsync(
            Tenant,
            new PlatformStreamInfoUpdate(Tags: ["chill"])
        );

        withCategory.IsFailure.Should().BeTrue();
        withCategory.ErrorCode.Should().Be("VALIDATION_FAILED");
        withTags.IsFailure.Should().BeTrue();
        withTags.ErrorCode.Should().Be("VALIDATION_FAILED");
        await client
            .DidNotReceiveWithAnyArgs()
            .UpdateActiveBroadcastTitleAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_token_fails_honestly()
    {
        (
            YouTubePlatformApi api,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatClient client
        ) = Build(token: null);
        sessions.SetLive(Tenant, Primary, "chat-42");

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new PlatformStreamInfoUpdate(Title: "t")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
        await client
            .DidNotReceiveWithAnyArgs()
            .UpdateActiveBroadcastTitleAsync(default!, default!, Arg.Any<CancellationToken>());
    }
}
