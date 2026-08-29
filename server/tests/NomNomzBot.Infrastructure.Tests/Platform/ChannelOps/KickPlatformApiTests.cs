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
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Infrastructure.Platform.ChannelOps;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.ChannelOps;

/// <summary>
/// Proves the Kick half of the channel-ops seam (S027): a title change actually reaches Kick's
/// <c>PATCH /public/v1/channels</c> via <see cref="IKickApiClient.UpdateChannelAsync"/> on the streamer's
/// own vaulted token, and a subsequent channel read reflects the new title — a real state change, not
/// merely a 200 status. A requested category resolves through a channel read to Kick's numeric id (Kick's
/// public API has no category-search endpoint); an unresolvable category and any tags are rejected
/// honestly rather than dropped.
/// </summary>
public sealed class KickPlatformApiTests
{
    private static readonly Guid BroadcasterId = Guid.Parse("0192c000-0000-7000-8000-0000000000e1");
    private const long KickUserId = 12345;
    private const string Token = "kick-bearer-1";

    private static (KickPlatformApi Api, IKickApiClient Client, FakeKickChannelStore Store) Build()
    {
        FakeKickChannelStore store = new("Old title", "Just Chatting", categoryId: 5);

        IKickAccessTokenProvider tokens = Substitute.For<IKickAccessTokenProvider>();
        tokens
            .GetAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(new KickAccess(Token, KickUserId));

        IKickApiClient client = Substitute.For<IKickApiClient>();
        client
            .GetChannelAsync(Token, KickUserId, Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success(store.Read()));
        client
            .UpdateChannelAsync(
                Token,
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                store.Apply(callInfo.ArgAt<string?>(1), callInfo.ArgAt<int?>(2));
                return Result.Success();
            });

        return (new KickPlatformApi(tokens, client), client, store);
    }

    [Fact]
    public async Task Provider_key_is_kick()
    {
        (KickPlatformApi api, _, _) = Build();
        api.Provider.Should().Be(NomNomzBot.Domain.Identity.Enums.AuthEnums.Platform.Kick);
    }

    [Fact]
    public async Task Updating_the_title_actually_changes_what_a_subsequent_read_returns()
    {
        (KickPlatformApi api, IKickApiClient client, FakeKickChannelStore store) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            BroadcasterId,
            new PlatformStreamInfoUpdate(Title: "New title!")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("New title!");

        // The state change, not just a 200: read the channel back through the same client and see the
        // new title actually persisted on Kick's side.
        Result<KickChannel> reread = await client.GetChannelAsync(Token, KickUserId);
        reread.Value.StreamTitle.Should().Be("New title!");
        store.PatchCallCount.Should().Be(1);
    }

    [Fact]
    public async Task An_exact_matching_category_name_resolves_to_its_current_kick_id()
    {
        (KickPlatformApi api, IKickApiClient client, _) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            BroadcasterId,
            new PlatformStreamInfoUpdate(CategoryName: "Just Chatting")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CategoryName.Should().Be("Just Chatting");
        await client.Received(1).UpdateChannelAsync(Token, null, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unresolvable_category_is_rejected_never_silently_dropped()
    {
        (KickPlatformApi api, IKickApiClient client, _) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            BroadcasterId,
            new PlatformStreamInfoUpdate(CategoryName: "Some Category Kick Doesn't Have")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await client
            .DidNotReceiveWithAnyArgs()
            .UpdateChannelAsync(default!, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tags_are_rejected_kick_has_no_tags_concept()
    {
        (KickPlatformApi api, _, _) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            BroadcasterId,
            new PlatformStreamInfoUpdate(Tags: ["speedrun"])
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task No_usable_token_fails_honestly_with_missing_scope()
    {
        IKickAccessTokenProvider tokens = Substitute.For<IKickAccessTokenProvider>();
        tokens.GetAsync(BroadcasterId, Arg.Any<CancellationToken>()).Returns((KickAccess?)null);
        IKickApiClient client = Substitute.For<IKickApiClient>();
        KickPlatformApi api = new(tokens, client);

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            BroadcasterId,
            new PlatformStreamInfoUpdate(Title: "t")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    /// <summary>A tiny in-memory Kick channel, mutated by <see cref="IKickApiClient.UpdateChannelAsync"/>
    /// and read back by <see cref="IKickApiClient.GetChannelAsync"/> — proves a title change is an actual
    /// state change on subsequent reads, not merely a successful call.</summary>
    private sealed class FakeKickChannelStore(string title, string categoryName, int categoryId)
    {
        private string _title = title;
        private readonly string _categoryName = categoryName;
        private readonly int _categoryId = categoryId;

        public int PatchCallCount { get; private set; }

        public KickChannel Read() =>
            new(KickUserId, _title, _categoryName, _categoryId, IsLive: true, ViewerCount: 10);

        public void Apply(string? newTitle, int? _)
        {
            PatchCallCount++;
            if (newTitle is not null)
                _title = newTitle;
        }
    }
}
