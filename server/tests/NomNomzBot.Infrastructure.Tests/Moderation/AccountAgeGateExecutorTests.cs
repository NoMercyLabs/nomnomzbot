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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// What the newcomer gate does once it has decided: remove the message, and for a hold, leave it where
/// a moderator can approve it.
/// </summary>
public class AccountAgeGateExecutorTests
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");

    private readonly ITwitchModerationApi _twitch = Substitute.For<ITwitchModerationApi>();
    private readonly IModerationQueueService _queue = Substitute.For<IModerationQueueService>();

    public AccountAgeGateExecutorTests()
    {
        _twitch
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _queue
            .EnqueueHeldMessageAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<ModerationQueueSource>()
            )
            .Returns(Result.Success(Guid.NewGuid()));
    }

    private AccountAgeGateExecutor NewExecutor() =>
        new(_twitch, _queue, NullLogger<AccountAgeGateExecutor>.Instance);

    private static SpamDecision Decision(bool dryRun) =>
        new(
            dryRun ? SpamOutcome.None : SpamOutcome.DeleteAndQueue,
            SpamOutcome.DeleteAndQueue,
            dryRun,
            "gate"
        );

    private static AccountAgeGateVerdict Verdict(bool holds) =>
        new(AccountAgeGateKind.AccountTooYoung, holds, "Account younger than 7 days.");

    private Task<SpamEnforcementOutcome> RunAsync(
        SpamDecision decision,
        AccountAgeGateVerdict verdict,
        string provider = AuthEnums.Platform.Twitch
    ) =>
        NewExecutor()
            .ExecuteAsync(
                Channel,
                provider,
                "msg-1",
                "viewer-1",
                "viewer_one",
                "buy followers",
                decision,
                verdict
            );

    [Fact]
    public async Task AHold_RemovesTheMessage_AndQueuesItWithTheGateAsSource()
    {
        SpamEnforcementOutcome outcome = await RunAsync(Decision(false), Verdict(holds: true));

        outcome.DeletedMessage.Should().BeTrue();
        await _twitch
            .Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        await _queue
            .Received(1)
            .EnqueueHeldMessageAsync(
                Channel,
                "msg-1",
                "viewer-1",
                "viewer_one",
                "buy followers",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                ModerationQueueSource.AccountAgeGate
            );
    }

    [Fact]
    public async Task ARemove_DeletesTheMessage_AndQueuesNothing()
    {
        SpamEnforcementOutcome outcome = await RunAsync(Decision(false), Verdict(holds: false));

        outcome.DeletedMessage.Should().BeTrue();
        await _queue
            .DidNotReceiveWithAnyArgs()
            .EnqueueHeldMessageAsync(default, default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task InDryRun_NothingIsDeletedOrQueued()
    {
        SpamEnforcementOutcome outcome = await RunAsync(Decision(true), Verdict(holds: true));

        outcome.DeletedMessage.Should().BeFalse();
        outcome.Skipped.Should().Be("dry run");
        await _twitch.DidNotReceiveWithAnyArgs().DeleteChatMessageAsync(default, default!, default);
        await _queue
            .DidNotReceiveWithAnyArgs()
            .EnqueueHeldMessageAsync(default, default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task OnAPlatformWithoutAnEnforcementPath_NothingHappens()
    {
        SpamEnforcementOutcome outcome = await RunAsync(
            Decision(false),
            Verdict(holds: true),
            provider: "kick"
        );

        outcome.DeletedMessage.Should().BeFalse();
        outcome.Skipped.Should().Contain("kick");
        await _queue
            .DidNotReceiveWithAnyArgs()
            .EnqueueHeldMessageAsync(default, default!, default!, default!, default!, default!);
    }
}
