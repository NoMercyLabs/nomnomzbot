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

namespace NomNomzBot.E2E.Tests.PublicSurfaces;

/// <summary>
/// Proves the harness drives a real-DOM public surface end to end through the idiomatic
/// <see cref="PageTest"/> base (a fresh browser context + page per test). The overlay host at
/// <c>/overlay</c> is served anonymously and, with no token supplied, returns a deterministic placeholder
/// page — plain HTML, so ordinary Playwright DOM locators apply directly (unlike the Compose canvas the
/// dashboard renders into). This is the harness's control: if this passes, Playwright, the driver, and the
/// browser are all wired up correctly.
/// </summary>
public sealed class PublicSurfaceDomTests : PageTest
{
    [E2EFact]
    public async Task Overlay_host_without_token_serves_the_placeholder_surface()
    {
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/overlay",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
        );

        // The placeholder page carries a single #m element with a fixed message — a stable DOM anchor.
        ILocator message = Page.Locator("#m");
        await Assertions.Expect(message).ToHaveTextAsync("This overlay URL is missing its token.");
    }
}
