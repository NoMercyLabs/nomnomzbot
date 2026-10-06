// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The Console panel of the widget editor: every console call of the preview lands as one row with its level (as
/// text, not only colour), a time and the source line. An object argument reads as JSON, Clear empties the panel,
/// and a log loop is capped so it cannot freeze the editor.
/// </summary>
public sealed class EditorConsoleTests : EditorPageTest
{
    // The console calls sit on lines 4 to 9 of the widget source.
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p>
        <script>
        console.log('plain log');
        console.info('an info');
        console.warn('a warning');
        console.error('an error');
        console.debug('a debug');
        console.log({ user: 'kitte', n: 2 });
        </script></body></html>
        """;

    private const string LoopWidget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p>
        <script>
        for (var i = 0; i < 600; i++) console.log('loop ' + i);
        </script></body></html>
        """;

    [E2EFact]
    public async Task Every_console_level_is_one_row_with_its_level_a_time_and_the_source_line()
    {
        await OpenWidgetAsync(Widget);

        ILocator rows = Page.Locator("#consoleList .console-row");
        await Expect(rows).ToHaveCountAsync(6);
        await Expect(rows.Locator(".console-level"))
            .ToHaveTextAsync(["log", "info", "warn", "error", "debug", "log"]);
        await Expect(rows.Locator(".console-text"))
            .ToHaveTextAsync([
                "plain log",
                "an info",
                "a warning",
                "an error",
                "a debug",
                "{\"user\":\"kitte\",\"n\":2}",
            ]);
        await Expect(rows.Locator(".console-source"))
            .ToHaveTextAsync([
                "index.html:4",
                "index.html:5",
                "index.html:6",
                "index.html:7",
                "index.html:8",
                "index.html:9",
            ]);
        await Expect(rows.Locator(".console-time").First)
            .ToHaveTextAsync(new Regex(@"^\d{1,2}:\d{2}:\d{2}"));
        await Expect(rows.Nth(2)).ToHaveAttributeAsync("data-level", "warn");
        await Expect(rows.Nth(3)).ToHaveAttributeAsync("data-level", "error");
    }

    [E2EFact]
    public async Task Clear_empties_the_panel()
    {
        await OpenWidgetAsync(Widget);
        await Expect(Page.Locator("#consoleList .console-row")).ToHaveCountAsync(6);

        await Page.Locator("#consoleClear").ClickAsync();

        await Expect(Page.Locator("#consoleList .console-row")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#consoleEmpty")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_log_loop_keeps_only_the_last_500_rows()
    {
        await OpenWidgetAsync(LoopWidget);

        ILocator rows = Page.Locator("#consoleList .console-row");
        await Expect(rows.Last).ToHaveAttributeAsync("data-text", "loop 599");
        await Expect(rows).ToHaveCountAsync(500);
        await Expect(rows.First.Locator(".console-text")).ToHaveTextAsync("loop 100");
    }

    private async Task OpenWidgetAsync(string source)
    {
        await ServeEditorFromTheWorkingTreeAsync();
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            (source) => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Console',
                    language: 'html',
                    entry: 'index.html',
                    files: { 'index.html': source },
                    sdkTypes: '',
                    fireSamples: {},
                    eventSubscriptions: [],
                    widget: { id: 'w-1', name: 'Alerts', settings: {} },
                },
            }, window.location.origin)
            """,
            source
        );
        await Page.Locator(".activity-item[data-view='run']").ClickAsync();
        await Expect(Page.FrameLocator("#previewFrame").Locator("#ready"))
            .ToHaveTextAsync("ready", new() { Timeout = 60_000 });
    }
}
