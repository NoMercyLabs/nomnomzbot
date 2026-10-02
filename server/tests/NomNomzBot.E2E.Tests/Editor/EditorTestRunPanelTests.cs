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
/// The script editor's Test run panel shows everything a dry run found out: the chat lines, the captured
/// effects, the variables the script set and its own console lines. This opens the real editor page and hands
/// it a test-run result the way the dashboard does.
/// </summary>
public sealed class EditorTestRunPanelTests : PageTest
{
    [E2EFact]
    public async Task The_panel_shows_the_variables_the_script_set_and_its_console_lines()
    {
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            () => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Script',
                    language: 'script',
                    entry: 'index.ts',
                    files: { 'index.ts': "console.log('hi');" },
                    sdkTypes: '',
                    testRunEnabled: true,
                },
            }, window.location.origin)
            """
        );
        await Page.Locator(".activity-item[data-view='run']").ClickAsync();
        await Expect(Page.Locator("#testRun")).ToBeVisibleAsync();

        await Page.EvaluateAsync(
            """
            () => window.postMessage({
                type: 'nnz:editor:testRunResult',
                ok: true,
                success: true,
                durationMs: 3,
                hostCallCount: 0,
                error: null,
                chatOutput: ['hello'],
                effects: [],
                variablesSet: { mood: 'happy' },
                console: ['mood is happy', 'warn: careful'],
            }, window.location.origin)
            """
        );

        ILocator result = Page.Locator("#testRunResult");
        await Expect(result).ToContainTextAsync("Chat output:\nhello");
        await Expect(result).ToContainTextAsync("Variables set:\nmood = happy");
        await Expect(result).ToContainTextAsync("Console:\nmood is happy\nwarn: careful");
    }
}
