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
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace NomNomzBot.E2E.Tests.Harness;

/// <summary>
/// The shared half of the editor type-checking tests: opening the real editor page with a project, reading the
/// diagnostics Monaco's TypeScript worker reports, and the auth of the instance under test.
/// </summary>
public abstract class EditorPageTest : PageTest
{
    protected static Dictionary<string, string> AuthHeaders() =>
        new() { ["Authorization"] = $"Bearer {E2ESettings.Token}" };

    // The 'tenant' claim of the E2E token: the broadcaster the token acts for.
    protected static string TokenTenant()
    {
        string payload = E2ESettings.Token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return claims.RootElement.GetProperty("tenant").GetString()!;
    }

    protected async Task<IReadOnlyList<int>> DiagnosticLinesAsync(string path) =>
        await Page.EvaluateAsync<int[]>(
            """
            (path) => window.monaco.editor
                .getModelMarkers({ resource: window.monaco.Uri.parse('file:///' + path) })
                .map((marker) => marker.startLineNumber)
            """,
            path
        );

    protected async Task OpenAsync(
        string language,
        string entry,
        string source,
        string sdkTypes,
        bool sdkTypesUnavailable = false
    )
    {
        await ServeEditorFromTheWorkingTreeAsync();
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            ([language, entry, source, sdkTypes, sdkTypesUnavailable]) => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Type check',
                    language,
                    entry,
                    files: { [entry]: source },
                    sdkTypes,
                    ...(sdkTypesUnavailable ? { sdkTypesUnavailable: true } : {}),
                },
            }, window.location.origin)
            """,
            new object[] { language, entry, source, sdkTypes, sdkTypesUnavailable }
        );
    }

    private bool _editorRouted;

    /// <summary>
    /// Serves <c>/editor/*</c> from the working tree's <c>Assets/editor</c> folder, so these tests check the editor
    /// code in the tree and not whichever build the instance under test runs. Everything else (the SDK types
    /// endpoint included) still goes to the instance.
    /// </summary>
    private async Task ServeEditorFromTheWorkingTreeAsync()
    {
        if (_editorRouted)
            return;
        _editorRouted = true;

        string folder = EditorAssetsFolder();
        await Page.RouteAsync(
            new Regex(@"^" + Regex.Escape(E2ESettings.BaseUrl) + @"/editor/[^?]*(\?.*)?$"),
            async route =>
            {
                string name = new Uri(route.Request.Url).AbsolutePath["/editor/".Length..];
                string file = Path.Combine(folder, name.Length == 0 ? "index.html" : name);
                if (!File.Exists(file) || Path.GetDirectoryName(file) != folder)
                {
                    await route.ContinueAsync();
                    return;
                }

                await route.FulfillAsync(
                    new()
                    {
                        BodyBytes = await File.ReadAllBytesAsync(file),
                        ContentType = Path.GetExtension(file) switch
                        {
                            ".html" => "text/html; charset=utf-8",
                            ".js" => "text/javascript; charset=utf-8",
                            ".css" => "text/css; charset=utf-8",
                            _ => "application/octet-stream",
                        },
                    }
                );
            }
        );
    }

    private static string EditorAssetsFolder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (
            directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, "server", "src"))
        )
            directory = directory.Parent;

        return directory is null
            ? throw new DirectoryNotFoundException(
                "No folder above the test assembly holds server/src."
            )
            : Path.Combine(
                directory.FullName,
                "server",
                "src",
                "NomNomzBot.Api",
                "Assets",
                "editor"
            );
    }

    // The files named by the Problems panel rows, once per row.
    protected async Task<IReadOnlyList<string>> ProblemFilesAsync()
    {
        await Page.WaitForTimeoutAsync(1_500);
        return await Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#problems .problem-where')]
                .map((el) => el.textContent.split(':')[0])
            """
        );
    }

    // Errors the TypeScript worker itself reports for a model: the truth behind a "no markers" check, which
    // would also pass when the worker had not answered yet. Each entry names the line, the code and the source
    // text, so a failing assertion shows what to fix.
    protected async Task<IReadOnlyList<string>> WorkerDiagnosticsAsync(string path)
    {
        await Page.WaitForFunctionAsync(
            "(path) => window.monaco?.editor.getModel(window.monaco.Uri.parse('file:///' + path))",
            path,
            new() { Timeout = 60_000 }
        );
        return await Page.EvaluateAsync<string[]>(
            """
            async (path) => {
                const uri = window.monaco.Uri.parse('file:///' + path);
                const model = window.monaco.editor.getModel(uri);
                const getWorker = await window.monaco.languages.typescript.getTypeScriptWorker();
                const worker = await getWorker(uri);
                const name = uri.toString();
                const all = [
                    ...(await worker.getSyntacticDiagnostics(name)),
                    ...(await worker.getSemanticDiagnostics(name)),
                ];
                const text = (m) => typeof m === 'string' ? m : (m?.messageText ?? '');
                return all.map((d) => {
                    const line = d.start === undefined ? 0 : model.getPositionAt(d.start).lineNumber;
                    const source = line ? model.getLineContent(line).trim() : '';
                    return `${line}: TS${d.code} ${text(d.messageText)} | ${source}`;
                });
            }
            """,
            path
        );
    }

    protected static void AssertNoProblems(IReadOnlyList<string> problems) =>
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

    /// <summary>
    /// The TypeScript diagnostic codes on <paramref name="path"/>, once at least <paramref name="expected"/>
    /// have arrived — the worker reports asynchronously, so an early read is empty, not clean.
    /// </summary>
    protected async Task<IReadOnlyList<string>> DiagnosticCodesAsync(string path, int expected)
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
