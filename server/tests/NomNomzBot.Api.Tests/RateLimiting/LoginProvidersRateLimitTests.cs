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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.RateLimiting;

namespace NomNomzBot.Api.Tests.RateLimiting;

/// <summary>
/// The login screen reads the provider list before any sign-in. In the strict "auth" bucket (10/min per IP)
/// a few page loads and retries locked a user out of the login page, which then claimed no login was set up.
/// The list is a public read, so it sits in the anonymous tier; the credential routes keep "auth".
/// </summary>
public sealed class LoginProvidersRateLimitTests
{
    [Fact]
    public void GetLoginProviders_IsInTheAnonymousTier_NotTheStrictAuthTier()
    {
        MethodInfo action = typeof(AuthController).GetMethod(
            nameof(AuthController.GetLoginProviders)
        )!;

        EnableRateLimitingAttribute? attribute =
            action.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false);

        attribute.Should().NotBeNull();
        attribute.PolicyName.Should().Be(RateLimitPolicyNames.Anonymous);
    }

    [Fact]
    public void TheCredentialRoutes_StayInTheStrictAuthTier()
    {
        MethodInfo link = typeof(AuthController).GetMethod(
            nameof(AuthController.StartIdentityLink)
        )!;

        EnableRateLimitingAttribute? attribute =
            link.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false);

        attribute.Should().NotBeNull();
        attribute.PolicyName.Should().Be(RateLimitPolicyNames.Auth);
    }
}
