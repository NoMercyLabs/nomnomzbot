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

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The editor preview runs a widget the way the overlay page does (owner 2026-10-02: "every obs and chat
/// exposable needs to be visible from the preview window"). These open the real editor page with a widget,
/// fire an event from the fire bar, and read both the widget's own page and the log of what it would do.
/// </summary>
public sealed class EditorPreviewTests : PageTest
{
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="out">waiting</p>
        <script>
        NomNomz.on('follow', function (d) {
            document.getElementById('out').textContent = NomNomz.widget.name + ' ' + NomNomz.settings.color + ' ' + d.user_name;
            NomNomz.actions.invoke('obs_switch_scene', { scene: 'BRB' });
        });
        NomNomz.on('cheer', function () { throw new Error('boom'); });
        </script></body></html>
        """;

    [E2EFact]
    public async Task A_fired_event_reaches_the_widget_and_the_action_it_would_run_is_logged()
    {
        await OpenAsync();

        await FireAsync("follow");

        await Expect(Page.FrameLocator("#previewFrame").Locator("#out"))
            .ToHaveTextAsync("Alerts red kitte");
        await Expect(Page.Locator("#previewLog li"))
            .ToHaveTextAsync(["Fired follow", "Would run obs_switch_scene {\"scene\":\"BRB\"}"]);
    }

    [E2EFact]
    public async Task A_handler_that_throws_is_shown_in_the_log()
    {
        await OpenAsync();

        await FireAsync("cheer");

        await Expect(Page.Locator("#previewLog li"))
            .ToHaveTextAsync(["Fired cheer", "on('cheer') handler: boom"]);
        await Expect(Page.Locator("#previewLog li[data-kind='error']")).ToHaveCountAsync(1);
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            (source) => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Preview',
                    language: 'html',
                    entry: 'index.html',
                    files: { 'index.html': source },
                    sdkTypes: '',
                    fireSamples: { follow: { user_name: 'kitte' }, cheer: { bits: 100 } },
                    eventSubscriptions: ['follow', 'cheer'],
                    widget: { id: 'w-1', name: 'Alerts', settings: { color: 'red' } },
                },
            }, window.location.origin)
            """,
            Widget
        );
        await Page.Locator(".activity-item[data-view='run']").ClickAsync();
    }

    private async Task FireAsync(string type)
    {
        // The frame must be rendered with the SDK before a fired event has a handler to reach.
        await Expect(Page.FrameLocator("#previewFrame").Locator("#out")).ToHaveTextAsync("waiting");
        await Page.Locator("#fireBar .fire-btn", new() { HasTextString = type }).ClickAsync();
    }
}
