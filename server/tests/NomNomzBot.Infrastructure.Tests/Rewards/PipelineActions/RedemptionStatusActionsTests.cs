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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Infrastructure.Rewards.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards.PipelineActions;

/// <summary>
/// Proves the redemption status actions operate on the TRIGGERING redemption through the SAME
/// <see cref="IRewardService.SetRedemptionStatusAsync"/> path the dashboard uses: fulfill sends FULFILLED,
/// refund sends CANCELED, the seeded <c>{redemption.id}</c> variable is honored when the context property is
/// absent (the generic event-response path), a non-redemption trigger is a typed failure that never reaches
/// the service, and a no-longer-pending redemption's service refusal surfaces as the action's failure.
/// </summary>
public sealed class RedemptionStatusActionsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b401");

    private static PipelineExecutionContext Ctx(
        string? redemptionId = null,
        string? seededVariable = null
    )
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RedemptionId = redemptionId,
            RawMessage = "",
        };
        if (seededVariable is not null)
            ctx.Variables["redemption.id"] = seededVariable;
        return ctx;
    }

    private static ActionDefinition Definition(string type) => new() { Type = type };

    [Fact]
    public async Task Fulfill_marks_the_triggering_redemption_fulfilled_through_the_service()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Channel.ToString(),
                "redemption-9",
                "FULFILLED",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ActionResult result = await new RedemptionFulfillAction(rewards).ExecuteAsync(
            Ctx(redemptionId: "redemption-9"),
            Definition("redemption_fulfill")
        );

        result.Succeeded.Should().BeTrue();
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Channel.ToString(),
                "redemption-9",
                "FULFILLED",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Refund_cancels_the_triggering_redemption_through_the_service()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Channel.ToString(),
                "redemption-9",
                "CANCELED",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ActionResult result = await new RedemptionRefundAction(rewards).ExecuteAsync(
            Ctx(redemptionId: "redemption-9"),
            Definition("redemption_refund")
        );

        result.Succeeded.Should().BeTrue();
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Channel.ToString(),
                "redemption-9",
                "CANCELED",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_seeded_redemption_variable_is_honored_when_the_context_property_is_absent()
    {
        // The generic event-response path carries the id only as the {redemption.id} variable
        // RewardRedeemedHandler seeds — the action must still find the triggering redemption.
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ActionResult result = await new RedemptionRefundAction(rewards).ExecuteAsync(
            Ctx(redemptionId: null, seededVariable: "redemption-var-3"),
            Definition("redemption_refund")
        );

        result.Succeeded.Should().BeTrue();
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Channel.ToString(),
                "redemption-var-3",
                "CANCELED",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_non_redemption_trigger_is_a_typed_failure_that_never_reaches_the_service()
    {
        IRewardService rewards = Substitute.For<IRewardService>();

        ActionResult fulfill = await new RedemptionFulfillAction(rewards).ExecuteAsync(
            Ctx(),
            Definition("redemption_fulfill")
        );
        ActionResult refund = await new RedemptionRefundAction(rewards).ExecuteAsync(
            Ctx(),
            Definition("redemption_refund")
        );

        fulfill.Succeeded.Should().BeFalse();
        fulfill.ErrorMessage.Should().Contain("redemption-triggered");
        refund.Succeeded.Should().BeFalse();
        refund.ErrorMessage.Should().Contain("redemption-triggered");
        await rewards
            .DidNotReceiveWithAnyArgs()
            .SetRedemptionStatusAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task A_no_longer_pending_redemption_surfaces_the_services_refusal()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "Redemption is not pending (already FULFILLED).",
                    "VALIDATION_FAILED"
                )
            );

        ActionResult result = await new RedemptionFulfillAction(rewards).ExecuteAsync(
            Ctx(redemptionId: "redemption-9"),
            Definition("redemption_fulfill")
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not pending");
    }
}
