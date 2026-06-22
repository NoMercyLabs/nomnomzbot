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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Infrastructure.Billing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the outbound Stripe gateway fails closed when Stripe is not configured (no <c>Stripe:SecretKey</c>) —
/// every outbound call returns <c>SERVICE_UNAVAILABLE</c> rather than constructing a client or throwing, so a
/// self-host / unconfigured deployment never 500s on a billing call. (The live Stripe round-trip is covered by an
/// integration check with test keys at deployment, not here.)
/// </summary>
public sealed class StripeGatewayTests
{
    private static StripeGateway Unconfigured() => new(Substitute.For<IConfiguration>());

    [Fact]
    public async Task Checkout_without_a_secret_key_fails_closed()
    {
        Result<CheckoutSessionDto> result = await Unconfigured()
            .CreateCheckoutSessionAsync("price_1", "broadcaster", "https://ok", "https://cancel");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task BillingPortal_without_a_secret_key_fails_closed()
    {
        Result<BillingPortalDto> result = await Unconfigured()
            .CreateBillingPortalSessionAsync("sub_1", "https://return");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }
}
