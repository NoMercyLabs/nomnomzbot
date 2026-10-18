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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Reaching any channel (chat-client.md §9a): a signed-in stranger with no row in a channel finds it in the
/// directory, and both viewer chat keys let them in. The directory carries public fields only.
/// </summary>
public sealed class ChannelDirectoryTests
{
    private static readonly Guid Fillz = Guid.Parse("0192a000-0000-7000-8000-0000000d0001");
    private static readonly Guid FillzOwner = Guid.Parse("0192a000-0000-7000-8000-0000000d0002");
    private static readonly Guid Banned = Guid.Parse("0192a000-0000-7000-8000-0000000d0003");
    private static readonly Guid BannedOwner = Guid.Parse("0192a000-0000-7000-8000-0000000d0004");
    private static readonly Guid Stranger = Guid.Parse("0192a000-0000-7000-8000-0000000d0005");
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<AuthDbContext> SeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.AddRange(
            new()
            {
                Id = FillzOwner,
                TwitchUserId = "tw-d1",
                Username = "fillz",
                UsernameNormalized = "fillz",
                DisplayName = "FillZ",
                ProfileImageUrl = "https://img/fillz.png",
            },
            new()
            {
                Id = BannedOwner,
                TwitchUserId = "tw-d2",
                Username = "fillzclone",
                UsernameNormalized = "fillzclone",
                DisplayName = "FillzClone",
            },
            new()
            {
                Id = Stranger,
                TwitchUserId = "tw-d3",
                Username = "stranger",
                UsernameNormalized = "stranger",
                DisplayName = "Stranger",
            }
        );
        db.Channels.AddRange(
            new()
            {
                Id = Fillz,
                OwnerUserId = FillzOwner,
                TwitchChannelId = "tw-d1",
                Name = "fillz",
                NameNormalized = "fillz",
                IsLive = true,
                IsOnboarded = true,
            },
            new()
            {
                Id = Banned,
                OwnerUserId = BannedOwner,
                TwitchChannelId = "tw-d2",
                Name = "fillzclone",
                NameNormalized = "fillzclone",
                Status = AuthEnums.ChannelStatus.Suspended,
            }
        );
        await new ActionDefinitionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
        return db;
    }

    private static ChannelService Service(AuthDbContext db) =>
        new(
            db,
            new FakeTimeProvider(Now),
            new RecordingEventBus(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchEventSubService>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<Application.Identity.Services.IBotModeratorStatusService>(),
            Substitute.For<Application.Platform.Services.ISupportContactService>()
        );

    [Theory]
    [InlineData("FILLZ")]
    [InlineData("ill")]
    public async Task A_stranger_finds_an_active_channel_by_name_and_never_a_suspended_one(
        string query
    )
    {
        AuthDbContext db = await SeededAsync();

        Result<PagedList<PublicChannelDto>> result = await Service(db)
            .SearchPublicChannelsAsync(query, new());

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        PublicChannelDto found = result.Value.Items.Single();
        found
            .Should()
            .Be(
                new PublicChannelDto(
                    Fillz.ToString(),
                    "fillz",
                    "FillZ",
                    "https://img/fillz.png",
                    true,
                    "twitch"
                )
            );
    }

    [Fact]
    public void The_directory_entry_carries_public_fields_only()
    {
        string[] fields = [.. typeof(PublicChannelDto).GetProperties().Select(p => p.Name)];

        fields
            .Should()
            .BeEquivalentTo([
                "Id",
                "Login",
                "DisplayName",
                "ProfileImageUrl",
                "IsLive",
                "Provider",
                "OnBot",
            ]);
    }

    [Theory]
    [InlineData("chat:viewer:read")]
    [InlineData("chat:viewer:send")]
    public async Task A_stranger_with_no_row_in_the_channel_passes_both_gates_for_viewer_chat(
        string actionKey
    )
    {
        AuthDbContext db = await SeededAsync();
        RoleResolver roles = new(
            db,
            new FakeTimeProvider(Now),
            Substitute.For<IActAsMembershipOverlay>(),
            Substitute.For<IViewAsScope>()
        );
        ActionAuthorizationService gate2 = new(
            db,
            roles,
            new RecordingEventBus(),
            new FakeTimeProvider(Now)
        );

        db.ChannelMemberships.Any(m => m.UserId == Stranger).Should().BeFalse();
        db.ChannelCommunityStandings.Any(s => s.UserId == Stranger).Should().BeFalse();

        (
            await new ChannelAccessService(db).CanResolveTenantAsync(
                Stranger.ToString(),
                Fillz.ToString()
            )
        )
            .Should()
            .BeTrue();

        Result<bool> allowed = await gate2.AuthorizeActionAsync(Stranger, Fillz, actionKey);

        allowed.IsSuccess.Should().BeTrue();
        allowed.Value.Should().BeTrue();
    }

    private static TwitchSearchChannel Hit(string id, string login, bool live) =>
        new(
            id,
            login,
            login.ToUpperInvariant(),
            "en",
            "",
            "",
            live,
            [],
            [],
            $"https://img/{login}",
            "",
            default
        );

    private static ChannelDirectoryService Directory(AuthDbContext db, ITwitchSearchApi search) =>
        new(Service(db), search);

    [Fact]
    public async Task A_search_appends_twitch_channels_off_the_bot_after_the_bot_channels()
    {
        AuthDbContext db = await SeededAsync();
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchChannelsAsync(
                "fillz",
                null,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchSearchChannel>(
                        [Hit("tw-d1", "FillZ", true), Hit("tw-x9", "fillzfan", false)],
                        null,
                        2
                    )
                )
            );

        Result<PagedList<PublicChannelDto>> result = await Directory(db, search)
            .SearchAsync("fillz", new());

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result
            .Value.Items.Should()
            .Equal(
                new PublicChannelDto(
                    Fillz.ToString(),
                    "fillz",
                    "FillZ",
                    "https://img/fillz.png",
                    true,
                    "twitch"
                ),
                new PublicChannelDto(
                    "tw-x9",
                    "fillzfan",
                    "FILLZFAN",
                    "https://img/fillzfan",
                    false,
                    "twitch",
                    OnBot: false
                )
            );
    }

    [Fact]
    public async Task A_twitch_failure_or_an_empty_search_keeps_only_the_bot_channels()
    {
        AuthDbContext db = await SeededAsync();
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchChannelsAsync(
                Arg.Any<string>(),
                null,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchSearchChannel>>("down", "TWITCH_UNAVAILABLE"));

        Result<PagedList<PublicChannelDto>> failed = await Directory(db, search)
            .SearchAsync("fillz", new());
        Result<PagedList<PublicChannelDto>> empty = await Directory(db, search)
            .SearchAsync("", new());

        failed.Value.Items.Should().ContainSingle().Which.OnBot.Should().BeTrue();
        empty.Value.Items.Should().OnlyContain(c => c.OnBot);
        await search
            .Received(1)
            .SearchChannelsAsync(
                Arg.Any<string>(),
                null,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }
}
