// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Onboarding;

/// <summary>
/// REQUIREMENT (first-time setup wizard): a brand-new operator, with no streamer account configured, must
/// be able to connect their Twitch account and land on the dashboard home. This is the first behavioural
/// E2E requirement — it asserts the rendered wizard exposes what the server's onboarding flow provides.
/// </summary>
public sealed class OnboardingRequirementTests : PageTest
{
    /// <summary>
    /// Pending an unattended-auth fixture. The wizard drives Twitch <b>Device Code Flow</b>:
    /// <c>POST /api/v1/auth/twitch/device</c> returns a <c>user_code</c> the operator approves on
    /// <c>twitch.tv/activate</c>, then <c>POST /api/v1/auth/twitch/device/poll</c> issues the JWTs. A
    /// browser cannot complete that human approval unattended, so the full connect → dashboard assertion
    /// cannot run in CI yet.
    ///
    /// <para>To turn this GREEN the harness needs ONE of:</para>
    /// <list type="bullet">
    ///   <item>a pre-minted session injected the way the app already supports — a JWT via the web
    ///   refresh-token HttpOnly cookie, or the <c>#access_token</c> bootstrap (see the project's
    ///   "E2E authed app access" note) — so the test starts already past onboarding; or</item>
    ///   <item>a device-code test double / fake Twitch that auto-approves outside Production, letting the
    ///   real wizard steps run end to end.</item>
    /// </list>
    ///
    /// <para>
    /// The body below encodes the intended a11y-driven steps (GetByRole + accessible name). Whether those
    /// role selectors resolve against the Compose canvas is exactly what
    /// <c>DashboardAccessibilityTests</c> determines; if Compose does not publish an a11y tree, these
    /// selectors switch to the canvas + screenshot / coordinate path documented there.
    /// </para>
    /// </summary>
    [Fact(
        Skip = "Requires an unattended-auth fixture (injected JWT session or a device-code test double). "
            + "See the class remarks; wire that up, then swap [Fact(Skip=…)] for [E2EFact]."
    )]
    public async Task Setup_wizard_connects_an_account_and_reaches_the_dashboard()
    {
        // 1. A fresh instance with no streamer account routes to the setup wizard.
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load }
        );
        await Page.WaitForFunctionAsync(
            "() => { const b = document.getElementById('nnz-boot'); return b !== null && b.style.display === 'none'; }"
        );

        // 2. The wizard's first step offers to connect the streamer's Twitch account (device-code flow).
        ILocator connect = Page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "Connect Twitch account" }
        );
        await Assertions.Expect(connect).ToBeVisibleAsync();
        await connect.ClickAsync();

        // 3. The device-code prompt appears (a user code to approve on twitch.tv/activate).
        //    >>> An unattended-auth fixture completes the approval here. <<<
        ILocator deviceCode = Page.GetByRole(
            AriaRole.Heading,
            new PageGetByRoleOptions { Name = "Activate on Twitch" }
        );
        await Assertions.Expect(deviceCode).ToBeVisibleAsync();

        // 4. After approval the app lands on the dashboard home — assert a home landmark is rendered.
        ILocator dashboardHome = Page.GetByRole(
            AriaRole.Heading,
            new PageGetByRoleOptions { Name = "Dashboard" }
        );
        await Assertions
            .Expect(dashboardHome)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    }
}
