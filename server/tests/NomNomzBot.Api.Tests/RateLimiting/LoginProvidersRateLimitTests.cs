// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
