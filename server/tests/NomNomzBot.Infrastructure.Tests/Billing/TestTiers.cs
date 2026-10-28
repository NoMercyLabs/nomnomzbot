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
