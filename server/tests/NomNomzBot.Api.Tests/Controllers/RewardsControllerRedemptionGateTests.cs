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
using FluentAssertions;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>Working the pending queue is moderator work, so each decision has its own moderator-level key.</summary>
public sealed class RewardsControllerRedemptionGateTests
{
    [Theory]
    [InlineData(nameof(RewardsController.FulfillRedemption), "reward:redemption:fulfill")]
    [InlineData(nameof(RewardsController.RefundRedemption), "reward:redemption:refund")]
    public void Each_queue_decision_is_gated_on_its_own_redemption_key(
        string method,
        string actionKey
    )
    {
        MethodInfo endpoint = typeof(RewardsController).GetMethod(method)!;

        endpoint.GetCustomAttribute<RequireActionAttribute>()!.ActionKey.Should().Be(actionKey);
    }
}
