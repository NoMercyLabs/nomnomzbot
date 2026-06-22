// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Infrastructure.Billing.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the <c>require_tier</c> pipeline action (monetization-billing.md §6): it continues when the channel's
/// entitlement meets the floor, fail-closed stops the pipeline (with the author's denied message) when it does
/// not, and rejects a misconfigured block with no <c>min_tier</c>.
/// </summary>
public sealed class RequireTierActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = Viewer.ToString(),
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!premium",
            CancellationToken = default,
        };

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "require_tier",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    [Fact]
    public async Task Continues_when_the_tier_floor_is_met()
    {
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .IsTierAtLeastAsync(Channel, "pro", Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));

        ActionResult result = await new RequireTierAction(tiers).ExecuteAsync(
            Context(),
            Action(("min_tier", "pro"))
        );

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Fail_closed_stops_with_the_denied_message_below_the_floor()
    {
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .IsTierAtLeastAsync(Channel, "premium", Arg.Any<CancellationToken>())
            .Returns(Result.Success(false));

        ActionResult result = await new RequireTierAction(tiers).ExecuteAsync(
            Context(),
            Action(("min_tier", "premium"), ("denied_message", "Premium only!"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Premium only!");
    }

    [Fact]
    public async Task Rejects_a_block_with_no_min_tier()
    {
        IBillingTierService tiers = Substitute.For<IBillingTierService>();

        ActionResult result = await new RequireTierAction(tiers).ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
    }
}
