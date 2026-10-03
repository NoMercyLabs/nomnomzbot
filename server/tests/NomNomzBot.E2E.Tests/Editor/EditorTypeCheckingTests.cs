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
/// The code editor must catch every type error before the code runs (owner 2026-10-02: "100% type safe").
/// These open the real editor page, hand it a project the way the dashboard does, and read the diagnostics
/// Monaco's TypeScript worker reports. A script runs in the Jint sandbox, which has no DOM; a widget runs in a
/// browser, and its plain JavaScript is checked as strictly as TypeScript.
/// </summary>
public sealed class EditorTypeCheckingTests : PageTest
{
    private const string ScriptTypes = "declare const bot: { args: string[] };";

    [E2EFact]
    public async Task A_script_is_checked_strictly()
    {
        await OpenAsync(
            "script",
            "index.ts",
            """
            const first = bot.args[0];
            first.length;
            function shout(text) { return text; }
            document.title;
            """,
            ScriptTypes
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.ts", expected: 3);

        // An argument the trigger did not pass is undefined.
        Assert.Contains("18048", codes);
        // A parameter without a type is an implicit any.
        Assert.Contains("7006", codes);
        // A script runs in the sandbox, where there is no DOM.
        Assert.Contains("2584", codes);
    }

    [E2EFact]
    public async Task A_widget_s_plain_javascript_is_type_checked_and_has_the_dom()
    {
        await OpenAsync(
            "vanilla-js",
            "index.js",
            """
            const count = 1;
            count.toUpperCase();
            document.title.toUpperCase();
            """,
            "declare const NomNomz: { reportError(message: string): void };"
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.js", expected: 1);

        // A number has no toUpperCase; a widget page does have a document.
        Assert.Equal(["2339"], codes);
    }

    [E2EFact]
    public async Task A_command_script_flags_a_variable_the_command_never_sets()
    {
        IAPIResponse types = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/sdk/types.d.ts?context=script&trigger=command",
            new()
            {
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {E2ESettings.Token}",
                },
            }
        );
        Assert.Equal(200, types.Status);

        await OpenAsync(
            "script",
            "index.ts",
            """
            bot.getVar('user.name');
            bot.getVar('typo');
            bot.getVar('typo', true);
            bot.getVar('args.2');
            """,
            await types.TextAsync()
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.ts", expected: 1);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.ts");

        // Only the unknown key is an error: a key the command sets, a key read with the dynamic flag, and a
        // positional argument all type-check.
        Assert.Single(codes);
        Assert.Equal([2], lines);
    }

    private async Task<IReadOnlyList<int>> DiagnosticLinesAsync(string path) =>
        await Page.EvaluateAsync<int[]>(
            """
            (path) => window.monaco.editor
                .getModelMarkers({ resource: window.monaco.Uri.parse('file:///' + path) })
                .map((marker) => marker.startLineNumber)
            """,
            path
        );

    private async Task OpenAsync(string language, string entry, string source, string sdkTypes)
    {
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            ([language, entry, source, sdkTypes]) => window.postMessage({
                type: 'nnz:editor:open',
                payload: { title: 'Type check', language, entry, files: { [entry]: source }, sdkTypes },
            }, window.location.origin)
            """,
            new object[] { language, entry, source, sdkTypes }
        );
    }

    /// <summary>
    /// The TypeScript diagnostic codes on <paramref name="path"/>, once at least <paramref name="expected"/>
    /// have arrived — the worker reports asynchronously, so an early read is empty, not clean.
    /// </summary>
    private async Task<IReadOnlyList<string>> DiagnosticCodesAsync(string path, int expected)
    {
        await Page.WaitForFunctionAsync(
            """
            ([path, expected]) => window.monaco?.editor
                .getModelMarkers({ resource: window.monaco.Uri.parse('file:///' + path) }).length >= expected
            """,
            new object[] { path, expected },
            new() { Timeout = 60_000 }
        );
        // A settled worker reports once more after the first batch; give it that beat before reading.
        await Page.WaitForTimeoutAsync(1_500);
        string[] codes = await Page.EvaluateAsync<string[]>(
            """
            (path) => window.monaco.editor
                .getModelMarkers({ resource: window.monaco.Uri.parse('file:///' + path) })
                .map((marker) => String(typeof marker.code === 'object' ? marker.code.value : marker.code))
            """,
            path
        );
        return codes;
    }
}
