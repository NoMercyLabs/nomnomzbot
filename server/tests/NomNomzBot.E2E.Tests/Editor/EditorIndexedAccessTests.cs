// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The editor's TypeScript check matches VS Code's defaults (owner 2026-10-07: a valid Vue widget showed 8 errors):
/// a plain array index read or a property read on a <c>Record&lt;string, string&gt;</c> is not an error. A real type
/// error in the same file must still show, so the test cannot pass with diagnostics switched off.
/// </summary>
public sealed class EditorIndexedAccessTests : EditorPageTest
{
    private const string Widget = """
        const list: { x: number }[] = [{ x: 1 }];
        const first: number = list[0].x;

        const cfg: { assets: Record<string, string> } = { assets: {} };
        const url: string = cfg.assets.someKey;

        const wrong: string = 5;
        """;

    private const int ErrorSeverity = 8;

    [E2EFact]
    public async Task Plain_index_reads_are_not_errors_but_a_real_type_error_still_is()
    {
        await OpenAsync("script", "widget.ts", Widget, sdkTypes: "");

        // The real error appears in the same worker pass as any indexed-access error would.
        await Page.WaitForFunctionAsync(
            "() => window.monaco && window.monaco.editor.getModelMarkers({ resource: window.monaco.Uri.parse('file:///widget.ts') }).length > 0",
            null,
            new() { Timeout = 60_000 }
        );
        await Page.WaitForTimeoutAsync(3_000);

        string[] errors = await Page.EvaluateAsync<string[]>(
            """
            (severity) => window.monaco.editor
                .getModelMarkers({ resource: window.monaco.Uri.parse('file:///widget.ts') })
                .filter((m) => m.severity === severity)
                .map((m) => `line ${m.startLineNumber} ${typeof m.code === 'object' ? m.code.value : m.code} ${m.message}`)
            """,
            ErrorSeverity
        );

        Assert.True(
            errors.Length == 1 && errors[0].StartsWith("line 7 "),
            "Expected exactly one error, on line 7 (the number assigned to a string). Got: "
                + string.Join(" | ", errors)
        );
    }
}
