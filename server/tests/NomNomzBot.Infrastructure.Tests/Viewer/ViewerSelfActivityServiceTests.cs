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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Services.Viewer;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Viewer;

/// <summary>
/// Proves the viewer's self-only message and command reads: the caller's own lines newest first with the
/// moderator-deletion flag and paging (another chatter's lines and another channel's lines never appear), and
/// the commands the caller's level may run, sorted by name, with the caller's own last use.
/// </summary>
public sealed class ViewerSelfActivityServiceTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-0000000007a1");
    private static readonly Guid OtherViewer = Guid.Parse("0192a000-0000-7000-8000-0000000007b2");
    private static readonly Guid Streamer = Guid.Parse("0192a000-0000-7000-8000-0000000007d1");
    private static readonly Guid ChannelOne = Guid.Parse("0192a000-0000-7000-8000-000000007c01");
    private static readonly Guid ChannelTwo = Guid.Parse("0192a000-0000-7000-8000-000000007c02");

    private sealed record Harness(
        ViewerSelfActivityService Sut,
        AuthDbContext Db,
        IBuiltinCommandService Builtins
    );

    private static DateTime At(int minute) => new(2026, 10, 14, 12, minute, 0, DateTimeKind.Utc);

    private static Harness Build(int callerLevel = 0)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(callerLevel));
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<BuiltinCommandDto>>([]));
        SeedWorld(db);
        return new(new(db, roles, builtins), db, builtins);
    }

    private static void SeedWorld(AuthDbContext db)
    {
        db.Users.Add(NewUser(Caller, "caller", "9001"));
        db.Users.Add(NewUser(OtherViewer, "other", "9002"));
        db.Users.Add(NewUser(Streamer, "streamer", "9003"));
        db.Channels.Add(NewChannel(ChannelOne, "alpha"));
        db.Channels.Add(NewChannel(ChannelTwo, "beta"));
        db.SaveChanges();
    }

    private static NomNomzBot.Domain.Identity.Entities.User NewUser(
        Guid id,
        string name,
        string twitchId
    ) =>
        new()
        {
            Id = id,
            TwitchUserId = twitchId,
            Username = name,
            UsernameNormalized = name,
            DisplayName = name.ToUpperInvariant(),
        };

    private static NomNomzBot.Domain.Identity.Entities.Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Streamer,
            TwitchChannelId = id.ToString("N")[..12],
            Name = name,
            NameNormalized = name,
        };

    private static void Say(
        AuthDbContext db,
        Guid channel,
        string twitchUserId,
        string text,
        DateTime at,
        DateTime? deletedAt = null
    )
    {
        db.ChatMessages.Add(
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                BroadcasterId = channel,
                Provider = "twitch",
                UserId = twitchUserId,
                Username = "u",
                DisplayName = "U",
                UserType = "viewer",
                Message = text,
                CreatedAt = at,
                DeletedAt = deletedAt,
            }
        );
        db.SaveChanges();
    }

    private static Command NewCommand(
        string name,
        int minLevel = 0,
        bool enabled = true,
        string? description = null,
        int cooldown = 0,
        Guid? channel = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel ?? ChannelOne,
            Name = name,
            NameNormalized = name,
            Tier = "template",
            MinPermissionLevel = minLevel,
            IsEnabled = enabled,
            Description = description,
            CooldownSeconds = cooldown,
        };

    [Fact]
    public async Task Messages_are_the_callers_own_newest_first_with_the_deleted_flag()
    {
        Harness h = Build();
        Say(h.Db, ChannelOne, "9001", "first", At(1));
        Say(h.Db, ChannelOne, "9001", "third", At(3));
        Say(h.Db, ChannelOne, "9001", "second removed", At(2), deletedAt: At(5));

        Result<PagedList<ViewerMessageDto>> result = await h.Sut.ListMessagesAsync(
            ChannelOne,
            Caller,
            1,
            25
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Items.Select(m => (m.Text, m.SentAt, m.DeletedByModerator))
            .Should()
            .Equal(
                ("third", At(3), false),
                ("second removed", At(2), true),
                ("first", At(1), false)
            );
        result.Value.Items.Should().OnlyContain(m => !string.IsNullOrEmpty(m.Id));
        result.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Messages_exclude_another_chatter_and_another_channel()
    {
        Harness h = Build();
        Say(h.Db, ChannelOne, "9001", "mine", At(1));
        Say(h.Db, ChannelOne, "9002", "someone else", At(2));
        Say(h.Db, ChannelTwo, "9001", "my line elsewhere", At(3));

        Result<PagedList<ViewerMessageDto>> result = await h.Sut.ListMessagesAsync(
            ChannelOne,
            Caller,
            1,
            25
        );

        result.Value.Items.Select(m => m.Text).Should().Equal("mine");
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Messages_page_through_newest_first()
    {
        Harness h = Build();
        for (int i = 1; i <= 5; i++)
            Say(h.Db, ChannelOne, "9001", $"m{i}", At(i));

        Result<PagedList<ViewerMessageDto>> second = await h.Sut.ListMessagesAsync(
            ChannelOne,
            Caller,
            2,
            2
        );

        second.Value.Items.Select(m => m.Text).Should().Equal("m3", "m2");
        second.Value.TotalCount.Should().Be(5);
        second.Value.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Messages_for_an_unknown_channel_is_not_found()
    {
        Harness h = Build();

        Result<PagedList<ViewerMessageDto>> result = await h.Sut.ListMessagesAsync(
            Guid.NewGuid(),
            Caller,
            1,
            25
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Commands_above_the_callers_level_or_disabled_are_left_out_and_the_rest_sort_by_name()
    {
        Harness h = Build(callerLevel: 2);
        h.Db.Commands.AddRange(
            NewCommand("zap", minLevel: 0, description: "last by name"),
            NewCommand("hug", minLevel: 2, description: "a hug", cooldown: 30),
            NewCommand("ban", minLevel: 10),
            NewCommand("off", minLevel: 0, enabled: false),
            NewCommand("elsewhere", minLevel: 0, channel: ChannelTwo)
        );
        await h.Db.SaveChangesAsync();

        Result<IReadOnlyList<ViewerCommandDto>> result = await h.Sut.ListCommandsAsync(
            ChannelOne,
            Caller
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(c => c.Name).Should().Equal("hug", "zap");
        ViewerCommandDto hug = result.Value[0];
        hug.Description.Should().Be("a hug");
        hug.CooldownSeconds.Should().Be(30);
        hug.LastUsedAt.Should().BeNull();
        result.Value[1].CooldownSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Built_ins_the_caller_may_run_are_listed_with_their_effective_cooldown()
    {
        Harness h = Build(callerLevel: 0);
        h.Builtins.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    Builtin("!uptime", "Everyone", 10, cooldownOverride: 45),
                    Builtin("!shutdown", "Moderator", 0),
                    Builtin("!quiet", "Everyone", 0, enabled: false),
                ])
            );

        Result<IReadOnlyList<ViewerCommandDto>> result = await h.Sut.ListCommandsAsync(
            ChannelOne,
            Caller
        );

        result.Value.Select(c => c.Name).Should().Equal("!uptime");
        result.Value[0].CooldownSeconds.Should().Be(45);
    }

    [Fact]
    public async Task Last_used_is_the_callers_own_newest_run_never_another_viewers()
    {
        Harness h = Build();
        h.Db.Commands.AddRange(NewCommand("hug"), NewCommand("wave"));
        h.Db.CommandUsages.AddRange(
            Usage(Caller, "hug", At(10)),
            Usage(Caller, "hug", At(30)),
            Usage(OtherViewer, "hug", At(50)),
            Usage(OtherViewer, "wave", At(51))
        );
        await h.Db.SaveChangesAsync();

        Result<IReadOnlyList<ViewerCommandDto>> result = await h.Sut.ListCommandsAsync(
            ChannelOne,
            Caller
        );

        result.Value.Single(c => c.Name == "hug").LastUsedAt.Should().Be(At(30));
        result.Value.Single(c => c.Name == "wave").LastUsedAt.Should().BeNull();
    }

    private static CommandUsage Usage(Guid viewer, string name, DateTime at) =>
        new()
        {
            BroadcasterId = ChannelOne,
            CommandNameSnapshot = name,
            ViewerUserId = viewer,
            ViewerProfileId = Guid.NewGuid(),
            WasSuccessful = true,
            CreatedAt = at,
        };

    private static BuiltinCommandDto Builtin(
        string name,
        string minLevel,
        int cooldown,
        bool enabled = true,
        int? cooldownOverride = null
    ) =>
        new(
            name.TrimStart('!'),
            name,
            enabled,
            cooldown,
            minLevel,
            "group",
            false,
            false,
            cooldownOverride,
            null,
            0
        );
}
