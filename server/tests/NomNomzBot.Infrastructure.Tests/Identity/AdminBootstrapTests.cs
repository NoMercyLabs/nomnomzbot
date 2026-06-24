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
/// Proves the first-admin bootstrap decision: the configured-admin opt-in (exact Twitch-id match, off when
/// unconfigured), the self-host convention (the first account onboarded is auto-promoted; later accounts and
/// SaaS are not), and idempotency (an existing principal is never re-promoted).
/// </summary>
public sealed class AdminBootstrapTests
{
    // ── Configured opt-in (any deployment) ────────────────────────────────────

    [Fact]
    public void Promotes_the_configured_admin_on_exact_match()
    {
        AdminBootstrap
            .ShouldPromote(
                false,
                "12345",
                "12345",
                isSelfHost: false,
                anyPlatformPrincipalExists: true
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Does_not_promote_a_non_matching_user()
    {
        AdminBootstrap
            .ShouldPromote(
                false,
                "12345",
                "99999",
                isSelfHost: false,
                anyPlatformPrincipalExists: true
            )
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_promote_a_saas_user_when_no_admin_is_configured(string? configured)
    {
        AdminBootstrap
            .ShouldPromote(
                false,
                configured,
                "12345",
                isSelfHost: false,
                anyPlatformPrincipalExists: false
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Is_idempotent_for_an_already_promoted_admin()
    {
        // Already a platform principal — a no-op even on an exact match AND as a self-host first owner.
        AdminBootstrap
            .ShouldPromote(
                true,
                "12345",
                "12345",
                isSelfHost: true,
                anyPlatformPrincipalExists: false
            )
            .Should()
            .BeFalse();
    }

    // ── Self-host convention (owner == admin, no config) ──────────────────────

    [Fact]
    public void Promotes_the_first_self_host_owner_when_no_admin_exists_yet()
    {
        AdminBootstrap
            .ShouldPromote(
                false,
                null,
                "12345",
                isSelfHost: true,
                anyPlatformPrincipalExists: false
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Does_not_promote_a_later_self_host_account_once_an_admin_exists()
    {
        AdminBootstrap
            .ShouldPromote(false, null, "99999", isSelfHost: true, anyPlatformPrincipalExists: true)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Does_not_auto_promote_the_first_saas_account()
    {
        // SaaS admins are pre-provisioned platform staff — onboarding never mints one.
        AdminBootstrap
            .ShouldPromote(
                false,
                null,
                "12345",
                isSelfHost: false,
                anyPlatformPrincipalExists: false
            )
            .Should()
            .BeFalse();
    }
}
