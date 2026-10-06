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
