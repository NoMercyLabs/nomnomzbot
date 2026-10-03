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
/// Close and Esc must never throw away unsaved edits. The editor page is top level in these tests, so every
/// message it posts to its host lands on its own window, where a recorder collects the message types.
/// </summary>
public sealed class EditorUnsavedChangesTests : EditorPageTest
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

    private async Task TypeAnEditAsync()
    {
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();
        await Page.Keyboard.TypeAsync("// my edit");
        await Page.WaitForTimeoutAsync(200);
    }

    private ILocator Confirm => Page.Locator("#unsavedBackdrop");

    [E2EFact]
    public async Task Esc_with_unsaved_edits_asks_first_and_keep_editing_leaves_the_edits()
    {
        await OpenRecordedAsync();
        await TypeAnEditAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Confirm).ToBeVisibleAsync();
        await Expect(Confirm.GetByRole(AriaRole.Button, new() { Name = "Keep editing" }))
            .ToBeVisibleAsync();
        await Expect(Confirm.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }))
            .ToBeVisibleAsync();
        Assert.DoesNotContain(CloseMessage, await PostedAsync());

        await Confirm.GetByRole(AriaRole.Button, new() { Name = "Keep editing" }).ClickAsync();

        await Expect(Confirm).ToBeHiddenAsync();
        Assert.DoesNotContain(CloseMessage, await PostedAsync());
        string text = await Page.EvaluateAsync<string>(
            "() => window.monaco.editor.getModel(window.monaco.Uri.parse('file:///index.js')).getValue()"
        );
        Assert.Contains("// my edit", text);
    }

    [E2EFact]
    public async Task Discard_posts_close_after_the_close_button_asked()
    {
        await OpenRecordedAsync();
        await TypeAnEditAsync();

        await Page.Locator("#close").ClickAsync();

        await Expect(Confirm).ToBeVisibleAsync();
        Assert.DoesNotContain(CloseMessage, await PostedAsync());

        await Confirm.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();

        Assert.Equal([CloseMessage], (await PostedAsync()).Where(type => type == CloseMessage));
    }

    [E2EFact]
    public async Task Esc_while_the_confirm_shows_keeps_editing_and_never_closes()
    {
        await OpenRecordedAsync();
        await TypeAnEditAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Confirm).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Confirm).ToBeHiddenAsync();
        Assert.DoesNotContain(CloseMessage, await PostedAsync());
    }

    [E2EFact]
    public async Task A_file_with_no_edits_closes_at_once_on_esc_and_on_the_close_button()
    {
        await OpenRecordedAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Page.Locator("#close").ClickAsync();

        Assert.Equal(
            [CloseMessage, CloseMessage],
            (await PostedAsync()).Where(type => type == CloseMessage)
        );
        await Expect(Confirm).ToBeHiddenAsync();
    }

    [E2EFact]
    public async Task After_a_saved_compile_the_edits_are_safe_and_esc_closes_at_once()
    {
        await OpenRecordedAsync();
        await TypeAnEditAsync();
        await Page.Locator("#save").ClickAsync();
        await Page.EvaluateAsync(
            "() => window.postMessage({ type: 'nnz:editor:compiled', ok: true, message: 'ok' }, window.location.origin)"
        );
        await Expect(Page.Locator("#save")).ToBeEnabledAsync();

        await Page.Keyboard.PressAsync("Escape");

        Assert.Equal([CloseMessage], (await PostedAsync()).Where(type => type == CloseMessage));
        await Expect(Confirm).ToBeHiddenAsync();
    }
}
