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
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>A person has ONE shoutout line per channel, so the API neither accepts nor returns a kind.</summary>
public sealed class ShoutoutOverrideApiHasNoKindTests
{
    [Theory]
    [InlineData(typeof(ModerationController.ShoutoutOverrideDto))]
    [InlineData(typeof(ModerationController.UpsertShoutoutOverrideRequest))]
    public void The_shoutout_override_contract_carries_no_kind(Type contract)
    {
        contract.GetProperty("Kind").Should().BeNull();
    }

    [Fact]
    public void Deleting_an_override_takes_no_kind_query_parameter()
    {
        typeof(ModerationController)
            .GetMethod(nameof(ModerationController.DeleteShoutoutOverride))!
            .GetParameters()
            .Select(p => p.Name)
            .Should()
            .NotContain("kind");
    }
}
