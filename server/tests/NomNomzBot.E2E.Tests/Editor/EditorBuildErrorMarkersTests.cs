// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// When the host's compile result carries build errors with positions, the editor underlines each one as a Monaco
/// error marker (owner 'nnz-build') on the named file, and the next compile result clears them.
/// </summary>
public sealed class EditorBuildErrorMarkersTests : EditorPageTest
{
    private const string Source = "const a = 1;\nconst b = 2;\nconst c = 3;\nconst d = 4;\n";

    private async Task OpenProjectAsync() =>
        await OpenAsync(
            "vanilla-js",
            "index.js",
            Source,
            "",
            extraFiles: new() { ["src/lib.js"] = "export const lib = 1;\nexport const two = 2;\n" }
        );

    private async Task PostCompiledAsync(string json)
    {
        await Page.EvaluateAsync(
            "(json) => window.postMessage({ type: 'nnz:editor:compiled', ...JSON.parse(json) }, window.location.origin)",
            json
        );
        await Page.WaitForTimeoutAsync(300);
    }

    private async Task<IReadOnlyList<BuildMarker>> MarkersAsync() =>
        (
            await Page.EvaluateAsync<JsonElement>(
                """
                () => window.monaco.editor.getModelMarkers({ owner: 'nnz-build' }).map((m) => ({
                    file: m.resource.path.replace(/^\//, ''),
                    line: m.startLineNumber,
                    column: m.startColumn,
                    endColumn: m.endColumn,
                    severity: m.severity,
                    message: m.message,
                }))
                """
            )
        ).Deserialize<List<BuildMarker>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private async Task<int> ModelCountAsync() =>
        await Page.EvaluateAsync<int>("() => window.monaco.editor.getModels().length");

    private sealed record BuildMarker(
        string File,
        int Line,
        int Column,
        int EndColumn,
        int Severity,
        string Message
    );

    [E2EFact]
    public async Task A_build_error_underlines_the_entry_file_at_its_line_and_column()
    {
        await OpenProjectAsync();
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();

        await PostCompiledAsync(
            """{ "ok": false, "message": "1 error", "errors": [ { "code": "CS1002", "message": "; expected", "file": "index.js", "line": 4, "column": 3 } ] }"""
        );

        IReadOnlyList<BuildMarker> markers = await MarkersAsync();
        BuildMarker marker = Assert.Single(markers);
        Assert.Equal("index.js", marker.File);
        Assert.Equal(4, marker.Line);
        Assert.Equal(3, marker.Column);
        Assert.True(marker.EndColumn > marker.Column);
        Assert.Equal(8, marker.Severity); // MarkerSeverity.Error
        Assert.Contains("; expected", marker.Message);
        await Expect(Page.Locator("#result")).ToContainTextAsync("1 error");
    }

    [E2EFact]
    public async Task An_error_in_a_second_project_file_marks_that_file()
    {
        await OpenProjectAsync();
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();

        await PostCompiledAsync(
            """{ "ok": false, "message": "2 errors", "errors": [ { "message": "bad import", "file": "src/lib.js", "line": 2, "column": 1 }, { "message": "no file", "line": 1 }, { "message": "no line", "file": "index.js" } ] }"""
        );

        IReadOnlyList<BuildMarker> markers = await MarkersAsync();
        Assert.Equal(2, markers.Count);
        BuildMarker lib = Assert.Single(markers, m => m.File == "src/lib.js");
        Assert.Equal(2, lib.Line);
        Assert.Equal(1, lib.Column);
        BuildMarker entry = Assert.Single(markers, m => m.File == "index.js");
        Assert.Equal(1, entry.Line);
        Assert.Equal(1, entry.Column);
        Assert.Equal("no file", entry.Message);
    }

    [E2EFact]
    public async Task The_next_compile_result_clears_the_build_markers()
    {
        await OpenProjectAsync();
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        await PostCompiledAsync(
            """{ "ok": false, "message": "1 error", "errors": [ { "message": "x", "file": "index.js", "line": 2, "column": 1 } ] }"""
        );
        Assert.Single(await MarkersAsync());

        await PostCompiledAsync("""{ "ok": true, "message": "compiled" }""");

        Assert.Empty(await MarkersAsync());
    }

    [E2EFact]
    public async Task An_error_naming_a_file_the_project_lacks_adds_no_marker_and_no_model()
    {
        await OpenProjectAsync();
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        int before = await ModelCountAsync();

        await PostCompiledAsync(
            """{ "ok": false, "message": "1 error", "errors": [ { "message": "x", "file": "nope/ghost.js", "line": 1, "column": 1 } ] }"""
        );

        Assert.Empty(await MarkersAsync());
        Assert.Equal(before, await ModelCountAsync());
    }
}
