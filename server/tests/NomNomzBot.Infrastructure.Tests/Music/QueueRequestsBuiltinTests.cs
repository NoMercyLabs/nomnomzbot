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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// <c>!closequeue</c> and <c>!openqueue</c>: only a music moderator changes the state, the asked state is the one
/// stored, and the reply says whether it changed, was already so, or failed.
/// </summary>
public sealed class QueueRequestsBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac0c1");

    private static BuiltinCommandContext Ctx() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserLogin = "moduser",
            TriggeringUserDisplayName = "ModUser",
            Args = "",
            RoleLevel = 4,
        };

    private static IBuiltinResponseComposer Composer()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Task.FromResult(call.ArgAt<string>(0)));
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

    private static ISongRequestIntakeService Intake(Result<bool> outcome)
    {
        ISongRequestIntakeService intake = Substitute.For<ISongRequestIntakeService>();
        intake
            .SetRequestsOpenAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(outcome);
        return intake;
    }

    [Fact]
    public async Task CloseQueue_without_the_grant_is_refused_and_changes_nothing()
    {
        ISongRequestIntakeService intake = Intake(Result.Success(true));
        CloseQueueBuiltin builtin = new(intake, Composer(), MusicGateTestKit.Gate(false));

        bool available = await builtin.IsAvailableToAsync(Ctx());
        Result<string> result = await builtin.ExecuteAsync(Ctx());

        available.Should().BeFalse();
        result.Value.Should().NotContain("closed");
        await intake.DidNotReceiveWithAnyArgs().SetRequestsOpenAsync(default!, default, default);
    }

    [Fact]
    public async Task CloseQueue_stores_closed_and_says_so()
    {
        ISongRequestIntakeService intake = Intake(Result.Success(true));
        CloseQueueBuiltin builtin = new(intake, Composer(), MusicGateTestKit.Gate(true));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Be("The song queue is now closed.");
        await intake.Received(1).SetRequestsOpenAsync(Broadcaster.ToString(), false, default);
    }

    [Fact]
    public async Task CloseQueue_on_a_closed_queue_says_it_was_already_closed()
    {
        CloseQueueBuiltin builtin = new(
            Intake(Result.Success(false)),
            Composer(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Be("The song queue is already closed.");
    }

    [Fact]
    public async Task CloseQueue_that_cannot_save_says_it_failed()
    {
        CloseQueueBuiltin builtin = new(
            Intake(Result.Failure<bool>("db down", "INTERNAL_ERROR")),
            Composer(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Be("Could not close the song queue. Try again in a moment.");
    }

    [Fact]
    public async Task OpenQueue_stores_open_and_says_so()
    {
        ISongRequestIntakeService intake = Intake(Result.Success(true));
        OpenQueueBuiltin builtin = new(intake, Composer(), MusicGateTestKit.Gate(true));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Be("The song queue is now open.");
        await intake.Received(1).SetRequestsOpenAsync(Broadcaster.ToString(), true, default);
    }

    [Fact]
    public async Task OpenQueue_on_an_open_queue_says_it_was_already_open()
    {
        OpenQueueBuiltin builtin = new(
            Intake(Result.Success(false)),
            Composer(),
            MusicGateTestKit.Gate(true)
        );

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Be("The song queue is already open.");
    }
}
