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

using FluentAssertions;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!clearsongs &lt;user&gt;</c>: only a music moderator removes a requester's queued songs, the name is
/// taken with or without an @, and the reply says how many went, that none were queued, or that it failed.
/// </summary>
public sealed class ClearSongsBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac0c1");

    private static BuiltinCommandContext Ctx(string args) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserLogin = "moduser",
            TriggeringUserDisplayName = "ModUser",
            Args = args,
            RoleLevel = 4,
        };

    private static IMusicService Music(int removed)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RemoveAllFromRequesterAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(removed);
        return music;
    }

    [Fact]
    public async Task Without_the_grant_it_is_refused_and_removes_nothing()
    {
        IMusicService music = Music(3);
        ClearSongsBuiltin builtin = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(false)
        );

        bool available = await builtin.IsAvailableToAsync(Ctx("viewer1"));
        Result<string> result = await builtin.ExecuteAsync(Ctx("viewer1"));

        available.Should().BeFalse();
        result.Value.Should().NotContain("viewer1");
        await music
            .DidNotReceiveWithAnyArgs()
            .RemoveAllFromRequesterAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("viewer1")]
    [InlineData("@viewer1")]
    [InlineData("  @Viewer1  ")]
    public async Task It_removes_that_requesters_songs_and_says_how_many(string args)
    {
        IMusicService music = Music(3);
        ClearSongsBuiltin builtin = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx(args));

        result
            .Value.Should()
            .Be("Removed 3 songs requested by " + args.Trim().TrimStart('@') + ".");
        await music
            .Received(1)
            .RemoveAllFromRequesterAsync(
                Broadcaster.ToString(),
                args.Trim().TrimStart('@'),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task It_says_when_that_user_had_nothing_queued()
    {
        ClearSongsBuiltin builtin = new(
            Music(0),
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx("viewer1"));

        result.Value.Should().Be("viewer1 has no songs in the queue.");
    }

    [Fact]
    public async Task Without_a_name_it_gives_the_usage_and_removes_nothing()
    {
        IMusicService music = Music(3);
        ClearSongsBuiltin builtin = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx("  "));

        result.Value.Should().Be("Usage: !clearsongs <user>");
        await music
            .DidNotReceiveWithAnyArgs()
            .RemoveAllFromRequesterAsync(default!, default!, default);
    }

    [Fact]
    public async Task When_the_removal_throws_it_says_it_failed()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RemoveAllFromRequesterAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<int>>(_ => throw new InvalidOperationException("provider down"));
        ClearSongsBuiltin builtin = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx("viewer1"));

        result.Value.Should().Be("Could not remove the songs of viewer1. Try again in a moment.");
    }
}
