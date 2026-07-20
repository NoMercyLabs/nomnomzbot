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
using NomNomzBot.E2E.Tests.Harness;
using Xunit.Abstractions;

namespace NomNomzBot.E2E.Tests.Dashboard;

/// <summary>
/// The core question this whole harness exists to answer: can Playwright see the Compose Multiplatform
/// dashboard, which renders to a single <c>&lt;canvas&gt;</c> rather than to semantic HTML?
///
/// <para>
/// This test drives its OWN browser (not the <c>PageTest</c> fixture) so it can pass the Chromium launch
/// flag <c>--force-renderer-accessibility</c> — the switch that asks the engine to build the full
/// accessibility tree, which is what nudges Compose/Skia into publishing its semantics as queryable DOM.
/// </para>
///
/// <para>
/// It establishes two independent proofs and records the finding for the second:
/// <list type="number">
///   <item>The Compose app mounts — a <c>canvas</c> locator resolves and is visible. This is a real
///   Playwright observation of the rendered dashboard and always holds once the app boots.</item>
///   <item>Whether Compose exposes an accessibility tree Playwright can query by ROLE
///   (<c>GetByRole</c> / accessible name). This is the experiment; the result is asserted softly and
///   written to test output plus a screenshot artifact, so requirement tests can decide whether to
///   assert the rendered UI via roles or fall back to the canvas + screenshot smoke path.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DashboardAccessibilityTests
{
    private readonly ITestOutputHelper _output;

    public DashboardAccessibilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [E2EFact]
    public async Task Compose_dashboard_mounts_and_we_probe_its_accessibility_tree()
    {
        using IPlaywright playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                // Force the engine to compute the accessibility tree — the trigger that makes a
                // canvas-based UI publish its semantics. Without it Chromium builds the AX tree lazily.
                Args = new[] { "--force-renderer-accessibility" },
            }
        );
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(
            $"{E2ESettings.BaseUrl}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load }
        );

        // App-ready signal baked into the boot page: the #nnz-boot overlay hides itself (display:none)
        // the instant Compose paints its first frame (window.__nnzAppReady). Waiting on that is far more
        // reliable than a fixed sleep or a network-idle guess.
        await page.WaitForFunctionAsync(
            "() => { const b = document.getElementById('nnz-boot'); return b !== null && b.style.display === 'none'; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 }
        );

        // PROOF 1 — Playwright observes the Compose render surface. The dashboard mounts into a <canvas>;
        // its visibility proves the app is up and Playwright can see it in the DOM.
        ILocator canvas = page.Locator("canvas");
        await Assertions
            .Expect(canvas.First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // PROOF 2 (experiment) — does Compose publish role-bearing accessibility nodes?
        // Count common interactive/landmark roles. Note the boot overlay itself declares role="status";
        // it is hidden by now, so GetByRole(status) is excluded here to keep the count app-authored.
        int buttons = await page.GetByRole(AriaRole.Button).CountAsync();
        int links = await page.GetByRole(AriaRole.Link).CountAsync();
        int headings = await page.GetByRole(AriaRole.Heading).CountAsync();
        int navigations = await page.GetByRole(AriaRole.Navigation).CountAsync();
        int textboxes = await page.GetByRole(AriaRole.Textbox).CountAsync();
        int semanticNodes = buttons + links + headings + navigations + textboxes;

        // Screenshot artifact either way, so the run is inspectable when a11y is inconclusive.
        string artifact = Path.Combine(AppContext.BaseDirectory, "dashboard-a11y-probe.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = artifact, FullPage = false });

        _output.WriteLine(
            $"A11Y PROBE against {E2ESettings.BaseUrl}/ — "
                + $"buttons={buttons} links={links} headings={headings} nav={navigations} textboxes={textboxes} "
                + $"=> total role-bearing nodes={semanticNodes}."
        );
        _output.WriteLine($"Screenshot artifact: {artifact}");

        if (semanticNodes > 0)
        {
            _output.WriteLine(
                "FINDING: Compose publishes a queryable accessibility tree. Requirement tests SHOULD "
                    + "assert the rendered dashboard via GetByRole / accessible name."
            );
        }
        else
        {
            _output.WriteLine(
                "FINDING: no role-bearing DOM nodes were exposed. Compose did not publish a queryable "
                    + "a11y tree in this configuration — requirement tests must fall back to the canvas + "
                    + "screenshot smoke path (or the app must opt into web a11y). The canvas proof above "
                    + "still confirms Playwright can see the dashboard."
            );
        }

        // Regardless of the a11y outcome, the harness has proven it can drive and observe the Compose
        // dashboard (canvas visible) and captured an inspectable artifact — the smoke floor never fails.
        Assert.True(
            new FileInfo(artifact).Length > 0,
            "Expected a non-empty screenshot of the rendered dashboard."
        );
    }
}
