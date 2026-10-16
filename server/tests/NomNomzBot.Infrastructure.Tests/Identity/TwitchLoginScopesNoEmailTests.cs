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
    }
}
