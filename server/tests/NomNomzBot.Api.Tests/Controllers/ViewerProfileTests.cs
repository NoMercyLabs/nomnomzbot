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

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.ValueObjects;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The public viewer profile (chat-client.md §9a "Public profile"): only what Twitch's viewer card shows; never
/// notes, warnings, bans, trust or the bot's counters.
/// </summary>
public sealed class ViewerProfileTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f11");
    private static readonly Guid Person = Guid.Parse("0192a000-0000-7000-8000-000000000f12");

    private static ViewerProfileController Build(
        CommunityControllerTestDbContext db,
        ITwitchChannelsApi? channels = null
    )
    {
        IChatEmoteCatalogue emotes = Substitute.For<IChatEmoteCatalogue>();
        emotes
            .GetForPersonAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ChatEmote>>([]));

        return new(db, channels ?? NotFollowing(), emotes);
    }

    private static ITwitchChannelsApi NotFollowing()
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<TwitchChannelFollower?>(null));

        return channels;
    }

    private static void AddPerson(CommunityControllerTestDbContext db) =>
        db.Users.Add(
            new()
            {
                Id = Person,
                TwitchUserId = "t-42",
                Username = "kitte",
                UsernameNormalized = "kitte",
                DisplayName = "Kitte",
                Color = "#FF00AA",
                AccountCreatedAt = new(2019, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            }
        );

    private static ViewerProfileController.PublicProfileDto Body(IActionResult result) =>
        (
            (StatusResponseDto<ViewerProfileController.PublicProfileDto>)
                ((OkObjectResult)result).Value!
        ).Data!;

    [Fact]
    public void The_profile_is_gated_by_the_viewer_read_key()
    {
        RequireActionAttribute gate = typeof(ViewerProfileController)
            .GetMethod(nameof(ViewerProfileController.GetProfile))!
            .GetCustomAttribute<RequireActionAttribute>()!;

        gate.ActionKey.Should().Be("chat:viewer:read");
    }

    [Fact]
    public async Task A_person_with_no_row_in_the_channel_still_has_a_public_profile()
    {
        CommunityControllerTestDbContext db = CommunityControllerTestDbContext.New();
        AddPerson(db);
        await db.SaveChangesAsync();

        IActionResult result = await Build(db).GetProfile(Channel.ToString(), "t-42");

        ViewerProfileController.PublicProfileDto body = Body(result);
        body.DisplayName.Should().Be("Kitte");
        body.Color.Should().Be("#FF00AA");
        body.AccountCreatedAt.Should().Be(new(2019, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        body.RecentMessages.Should().BeEmpty();
        body.Badges.Should().BeEmpty();
        body.SubTier.Should().BeNull();
        body.FollowedAt.Should().BeNull();
    }

    [Fact]
    public async Task Recent_lines_leave_out_deleted_lines_and_commands_and_badges_come_from_the_newest()
    {
        CommunityControllerTestDbContext db = CommunityControllerTestDbContext.New();
        AddPerson(db);
        db.ChatMessages.AddRange(
            Line("a", 0, badges: [new("subscriber", "12", "14")]),
            Line("b", 1, deleted: true),
            Line("c", 2, command: true),
            Line("d", 3, badges: [new("vip", "1")])
        );
        db.ViewerProfiles.Add(
            new()
            {
                BroadcasterId = Channel,
                ViewerUserId = Person,
                ViewerTwitchUserId = "t-42",
                IsSubscriber = true,
                SubTier = "1000",
                TotalWatchSeconds = 99_000,
            }
        );
        await db.SaveChangesAsync();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        DateTimeOffset followed = new(2024, 5, 6, 0, 0, 0, TimeSpan.Zero);
        channels
            .GetChannelFollowerAsync(Channel, "t-42", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(new("t-42", "kitte", "Kitte", followed))
            );

        ViewerProfileController.PublicProfileDto body = Body(
            await Build(db, channels).GetProfile(Channel.ToString(), Person.ToString())
        );

        body.RecentMessages.Select(m => m.Id).Should().Equal("d", "a");
        body.Badges.Should().ContainSingle().Which.SetId.Should().Be("vip");
        body.SubTier.Should().Be("1000");
        body.FollowedAt.Should().Be(followed);
    }

    [Fact]
    public async Task The_profile_carries_no_moderator_or_bot_counter_field()
    {
        CommunityControllerTestDbContext db = CommunityControllerTestDbContext.New();
        AddPerson(db);
        await db.SaveChangesAsync();

        ViewerProfileController.PublicProfileDto body = Body(
            await Build(db).GetProfile(Channel.ToString(), "t-42")
        );
        string json = JsonSerializer.Serialize(body).ToLowerInvariant();

        foreach (string hidden in Hidden)
            json.Should().NotContain(hidden);
    }

    private static readonly string[] Hidden =
    [
        "note",
        "warn",
        "ban",
        "timeout",
        "trust",
        "standing",
        "moderation",
        "watch",
    ];

    private static ChatMessage Line(
        string id,
        int second,
        bool deleted = false,
        bool command = false,
        List<ChatBadge>? badges = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = Channel,
            UserId = "t-42",
            Username = "kitte",
            DisplayName = "Kitte",
            UserType = "viewer",
            Message = id,
            IsCommand = command,
            Fragments = [],
            Badges = badges ?? [],
            CreatedAt = new(2026, 10, 16, 12, 0, second, DateTimeKind.Utc),
            DeletedAt = deleted ? new DateTime(2026, 10, 16, 12, 1, 0, DateTimeKind.Utc) : null,
        };
}
