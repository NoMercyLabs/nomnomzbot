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

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The host (the desktop window's X button) asks the editor to close with <c>nnz:editor:requestClose</c>. The
/// editor must answer exactly like its own Close button: confirm when edits are unsaved, close at once when not.
/// </summary>
public sealed class EditorHostCloseRequestTests : EditorPageTest
{
    private const string CloseMessage = "nnz:editor:close";

    private async Task OpenRecordedAsync()
    {
        await OpenAsync("vanilla-js", "index.js", "const a = 1;", "");
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        await Page.EvaluateAsync(
            """
            () => {
                window.__posted = [];
                window.addEventListener('message', (event) => {
                    const type = event.data?.type;
                    if (typeof type === 'string' && type.startsWith('nnz:editor:'))
                        window.__posted.push(type);
                });
            }
            """
        );
    }

    private async Task<IReadOnlyList<string>> PostedAsync()
    {
        await Page.WaitForTimeoutAsync(300);
        return await Page.EvaluateAsync<string[]>("() => window.__posted");
    }

    private Task HostRequestsCloseAsync() =>
        Page.EvaluateAsync(
            "() => window.postMessage({ type: 'nnz:editor:requestClose' }, window.location.origin)"
        );

    private ILocator Confirm => Page.Locator("#unsavedBackdrop");

    [E2EFact]
    public async Task Host_close_request_with_unsaved_edits_shows_the_confirm_and_posts_no_close()
    {
        await OpenRecordedAsync();
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();
        await Page.Keyboard.TypeAsync("// my edit");
        await Page.WaitForTimeoutAsync(200);

        await HostRequestsCloseAsync();

        await Expect(Confirm).ToBeVisibleAsync();
        Assert.DoesNotContain(CloseMessage, await PostedAsync());

        await Confirm.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();

        Assert.Equal([CloseMessage], (await PostedAsync()).Where(type => type == CloseMessage));
    }

    [E2EFact]
    public async Task Host_close_request_with_no_edits_posts_close_at_once()
    {
        await OpenRecordedAsync();

        await HostRequestsCloseAsync();

        Assert.Equal([CloseMessage], (await PostedAsync()).Where(type => type == CloseMessage));
        await Expect(Confirm).ToBeHiddenAsync();
    }
}
