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

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Infrastructure.Billing;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>The real unsubscribed-tier policy with the shipped settings (no plan means <c>base</c>).</summary>
internal static class TestUnsubscribedTier
{
    public static UnsubscribedTierPolicy Default() =>
        new(Options.Create(new BillingOptions()), NullLogger<UnsubscribedTierPolicy>.Instance);
}
