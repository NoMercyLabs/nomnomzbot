// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /channels/moderated</c> resolves "the channels I moderate" from the CALLER's own channel (their
/// identity), not from a <c>broadcaster_id</c> claim the JWT never mints — the defect that made this endpoint
/// always answer 401. Asserts the mapped DTO shape, the DB-backed onboarded flag, the resolution path, and that a
/// caller with no owned channel gets an empty 200 rather than the old 401.
/// </summary>
public sealed class ChannelsControllerModeratedTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid OwnChannel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    [Fact]
    public async Task GetModeratedChannels_resolves_the_callers_own_channel_and_maps_results()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            ApiTestDbContext db
        ) = Build();

        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);
        moderators
            .GetModeratedChannelsAsync(
                OwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [
                            new("999", "coolstreamer", "CoolStreamer"),
                            new("888", "otherstreamer", "OtherStreamer"),
                        ],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        // "999" is already onboarded in our DB; "888" is not — the DTO's IsOnboarded must reflect that.
        db.Channels.Add(
            new()
            {
                Id = OwnChannel,
                OwnerUserId = Caller,
                TwitchChannelId = "999",
                Name = "coolstreamer",
                NameNormalized = "coolstreamer",
                IsOnboarded = true,
            }
        );
        await db.SaveChangesAsync();

        IActionResult result = await controller.GetModeratedChannels(default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<List<ChannelsController.ModeratedChannelDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<ChannelsController.ModeratedChannelDto>>>()
            .Subject;

        body.Data.Should().HaveCount(2);
        body.Data.Should()
            .ContainSingle(d => d.Id == "999")
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Login = "coolstreamer",
                    DisplayName = "CoolStreamer",
                    IsOnboarded = true,
                }
            );
        body.Data.Should().ContainSingle(d => d.Id == "888").Which.IsOnboarded.Should().BeFalse();

        // It resolved via the caller's OWN channel Guid — never a (missing) broadcaster_id claim.
        await moderators
            .Received(1)
            .GetModeratedChannelsAsync(
                OwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetModeratedChannels_carries_the_twitch_picture_and_live_state_of_each_channel()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            _
        ) = Build();
        ModeratesTwoChannels(moderators, access);
        _users
            .GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "999", "888" })),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    TwitchUserWithPicture("999", "https://cdn.example/999.png"),
                ])
            );
        _streams
            .GetStreamsAsync(
                Arg.Is<TwitchStreamsFilter>(filter =>
                    filter.UserIds!.SequenceEqual(new[] { "999", "888" })
                ),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchStream>([LiveStream("888")], NextCursor: null, Total: 1)
                )
            );

        List<ChannelsController.ModeratedChannelDto> rows = await ModeratedRows(controller);

        rows.Single(row => row.Id == "999")
            .Should()
            .BeEquivalentTo(
                new { ProfileImageUrl = "https://cdn.example/999.png", IsLive = (bool?)false }
            );
        // Twitch gave no user record for 888: no picture, and the live state still stands.
        rows.Single(row => row.Id == "888")
            .Should()
            .BeEquivalentTo(new { ProfileImageUrl = (string?)null, IsLive = (bool?)true });
    }

    [Fact]
    public async Task GetModeratedChannels_reports_an_unknown_live_state_when_twitch_does_not_answer()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            _
        ) = Build();
        ModeratesTwoChannels(moderators, access);

        List<ChannelsController.ModeratedChannelDto> rows = await ModeratedRows(controller);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.ProfileImageUrl == null && row.IsLive == null);
    }

    private static void ModeratesTwoChannels(
        ITwitchModeratorsApi moderators,
        IChannelAccessService access
    )
    {
        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);
        moderators
            .GetModeratedChannelsAsync(
                OwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [
                            new("999", "coolstreamer", "CoolStreamer"),
                            new("888", "otherstreamer", "OtherStreamer"),
                        ],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );
    }

    private static async Task<List<ChannelsController.ModeratedChannelDto>> ModeratedRows(
        ChannelsController controller
    )
    {
        IActionResult result = await controller.GetModeratedChannels(default);
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;

        return ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<ChannelsController.ModeratedChannelDto>>>()
            .Subject.Data!;
    }

    private static TwitchUser TwitchUserWithPicture(string id, string picture) =>
        new(id, "login" + id, "Name" + id, "", "", "", picture, "", 0, DateTimeOffset.UnixEpoch);

    private static TwitchStream LiveStream(string userId) =>
        new(
            "stream" + userId,
            userId,
            "login" + userId,
            "Name" + userId,
            "",
            "",
            "live",
            "",
            [],
            0,
            DateTimeOffset.UnixEpoch,
            "en",
            "",
            false
        );

    [Fact]
    public async Task GetModeratedChannels_starts_the_callers_moderator_chat_readers()
    {
        (ChannelsController controller, _, IChannelAccessService access, _) = Build();
        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(Guid.Empty);

        await controller.GetModeratedChannels(default);

        await _moderatorChat.Received(1).RequestSyncAsync(Caller);
    }

    [Fact]
    public async Task GetModeratedChannels_with_no_owned_channel_returns_empty_not_401()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            _
        ) = Build();
        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(Guid.Empty);

        IActionResult result = await controller.GetModeratedChannels(default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<List<ChannelsController.ModeratedChannelDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<ChannelsController.ModeratedChannelDto>>>()
            .Subject;
        body.Data.Should().BeEmpty();

        // No owned channel → the moderators API is never queried, and the caller is NOT rejected (old 401 bug).
        await moderators.DidNotReceiveWithAnyArgs().GetModeratedChannelsAsync(default, default!);
    }

    private readonly Application.Chat.Services.IModeratorChatReaders _moderatorChat =
        Substitute.For<Application.Chat.Services.IModeratorChatReaders>();

    private readonly ITwitchUsersApi _users = Substitute.For<ITwitchUsersApi>();
    private readonly ITwitchStreamsApi _streams = Substitute.For<ITwitchStreamsApi>();

    private (
        ChannelsController Controller,
        ITwitchModeratorsApi Moderators,
        IChannelAccessService Access,
        ApiTestDbContext Db
    ) Build()
    {
        IChannelService service = Substitute.For<IChannelService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        ApiTestDbContext db = ApiTestDbContext.New();
        // Until a test says otherwise, Twitch does not answer the picture and live-state lookups.
        _users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("twitch is down"));
        _streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchStream>>("twitch is down"));

        ChannelsController controller = new(
            service,
            db,
            moderators,
            access,
            Substitute.For<Application.Contracts.Authorization.IMembershipService>(),
            Substitute.For<IUserService>(),
            Substitute.For<IChannelDeletePreviewService>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IViewAsScope>(),
            _moderatorChat,
            _users,
            _streams
        )
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new(
                        new ClaimsIdentity(
                            [new(ClaimTypes.NameIdentifier, Caller.ToString())],
                            "TestAuth"
                        )
                    ),
                },
            },
        };
        return (controller, moderators, access, db);
    }
}
