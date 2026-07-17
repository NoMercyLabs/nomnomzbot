// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Tier-limit stubs for creation-path quota tests. <see cref="Unlimited"/> mirrors self-host (every key
/// resolves -1); <see cref="WithLimit"/> layers one finite cap on top — the SaaS shape.
/// </summary>
internal static class TestTiers
{
    public static IBillingTierService Unlimited()
    {
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .GetLimitAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(-1L));
        return tiers;
    }

    public static IBillingTierService WithLimit(string limitKey, long value)
    {
        IBillingTierService tiers = Unlimited();
        tiers
            .GetLimitAsync(Arg.Any<Guid>(), limitKey, Arg.Any<CancellationToken>())
            .Returns(Result.Success(value));
        return tiers;
    }
}
