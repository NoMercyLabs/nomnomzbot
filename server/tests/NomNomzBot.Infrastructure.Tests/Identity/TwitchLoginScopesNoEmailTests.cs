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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// No sign-in asks for the user's email: the platform never stores one, so no login scope set may carry the
/// email scope (Stoney, 2026-10-16: "We don't need their email!").
/// </summary>
public sealed class TwitchLoginScopesNoEmailTests
{
    [Theory]
    [InlineData(TwitchLoginRole.Viewer)]
    [InlineData(TwitchLoginRole.Moderator)]
    [InlineData(TwitchLoginRole.Broadcaster)]
    public void Every_role_scope_set_has_no_email_scope(TwitchLoginRole role)
    {
        string[] scopes = TwitchLoginScopes.For(role);

        scopes.Should().NotBeEmpty();
        scopes
            .Should()
            .NotContain(
                scope => scope.Contains("email", StringComparison.OrdinalIgnoreCase),
                "no sign-in asks for the user's email"
            );
        // Twitch's consent screen lists the email for `openid` alone, and no sign-in reads an OIDC token.
        scopes
            .Should()
            .NotContain("openid", "openid makes Twitch show the email on the consent screen");
    }
}
