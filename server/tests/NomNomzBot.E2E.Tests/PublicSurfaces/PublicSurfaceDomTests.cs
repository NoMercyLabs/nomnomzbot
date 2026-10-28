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
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );

        // The placeholder page carries a single #m element with a fixed message — a stable DOM anchor.
        ILocator message = Page.Locator("#m");
        await Assertions.Expect(message).ToHaveTextAsync("This overlay URL is missing its token.");
    }
}
