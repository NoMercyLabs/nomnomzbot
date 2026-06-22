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
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the first-admin bootstrap decision: only the account whose Twitch id exactly matches the
/// configured <c>App:InitialAdminTwitchId</c> is promoted, the feature is off when unconfigured, and an
/// already-promoted admin is never re-promoted (idempotent).
/// </summary>
public sealed class AdminBootstrapTests
{
    [Fact]
    public void Promotes_the_configured_admin_on_exact_match()
    {
        AdminBootstrap.ShouldPromote(false, "12345", "12345").Should().BeTrue();
    }

    [Fact]
    public void Does_not_promote_a_non_matching_user()
    {
        AdminBootstrap.ShouldPromote(false, "12345", "99999").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_promote_when_no_admin_is_configured(string? configured)
    {
        AdminBootstrap.ShouldPromote(false, configured, "12345").Should().BeFalse();
    }

    [Fact]
    public void Is_idempotent_for_an_already_promoted_admin()
    {
        // Already a platform principal — a no-op even on an exact match, so the flag is never re-set.
        AdminBootstrap.ShouldPromote(true, "12345", "12345").Should().BeFalse();
    }
}
