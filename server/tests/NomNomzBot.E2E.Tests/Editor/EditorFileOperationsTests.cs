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
/// New, rename and delete happen in the page: an inline name field in the tree, a delete that can be undone, and a
/// confirm that traps focus. A native browser dialog (window.prompt / confirm) is a defect, so every test records
/// the dialogs the page opens and asserts there were none.
/// </summary>
public sealed class EditorFileOperationsTests : EditorPageTest
{
    private readonly List<string> _nativeDialogs = [];

    private ILocator Row(string name) =>
        Page.Locator("#fileList .file-row").Filter(new() { HasText = name });

    private ILocator NameField => Page.Locator("#fileList input.tree-input");

    private ILocator Toast => Page.Locator("#toast");

    private async Task OpenReadyAsync()
    {
        Page.Dialog += (_, dialog) =>
        {
            _nativeDialogs.Add($"{dialog.Type}: {dialog.Message}");
            _ = dialog.DismissAsync();
        };
        await OpenAsync(
            "vanilla-js",
            "index.js",
            "const a = 1;",
            "",
            extraFiles: new() { ["lib/helper.ts"] = "export const helper = 42;" }
        );
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        await Expect(Row("helper.ts")).ToBeVisibleAsync();
    }

    private Task<string> ModelTextAsync(string path) =>
        Page.EvaluateAsync<string>(
            "(path) => window.monaco.editor.getModel(window.monaco.Uri.parse('file:///' + path))?.getValue() ?? '<none>'",
            path
        );

    [E2EFact]
    public async Task New_file_takes_its_name_in_an_inline_field_and_opens_an_empty_file()
    {
        await OpenReadyAsync();

        await Page.Locator("#newFile").ClickAsync();
        await Expect(NameField).ToBeFocusedAsync();
        await NameField.FillAsync("lib/extra.ts");
        await NameField.PressAsync("Enter");

        await Expect(NameField).ToHaveCountAsync(0);
        await Expect(Row("extra.ts")).ToHaveAttributeAsync("aria-current", "true");
        Assert.Equal("", await ModelTextAsync("lib/extra.ts"));
        Assert.Empty(_nativeDialogs);
    }

    [E2EFact]
    public async Task New_file_with_a_taken_name_stays_open_and_says_why_and_esc_cancels()
    {
        await OpenReadyAsync();
        await Page.Locator("#newFile").ClickAsync();

        await NameField.FillAsync("lib/helper.ts");
        await NameField.PressAsync("Enter");

        await Expect(NameField).ToBeVisibleAsync();
        await Expect(Page.Locator("#fileList [role=alert]")).ToContainTextAsync("helper.ts");
        Assert.Equal("export const helper = 42;", await ModelTextAsync("lib/helper.ts"));

        await NameField.PressAsync("Escape");

        await Expect(NameField).ToHaveCountAsync(0);
        Assert.Empty(_nativeDialogs);
    }

    [E2EFact]
    public async Task Rename_edits_the_name_in_place_and_keeps_the_content()
    {
        await OpenReadyAsync();

        await Row("helper.ts").HoverAsync();
        await Row("helper.ts").GetByRole(AriaRole.Button, new() { Name = "Rename" }).ClickAsync();
        await Expect(NameField).ToHaveValueAsync("lib/helper.ts");
        await NameField.FillAsync("lib/util.ts");
        await NameField.PressAsync("Enter");

        await Expect(Row("util.ts")).ToBeVisibleAsync();
        await Expect(Row("helper.ts")).ToHaveCountAsync(0);
        await Row("util.ts").ClickAsync();
        Assert.Equal("export const helper = 42;", await ModelTextAsync("lib/util.ts"));
        Assert.Equal("<none>", await ModelTextAsync("lib/helper.ts"));
        Assert.Empty(_nativeDialogs);
    }

    [E2EFact]
    public async Task Delete_removes_the_file_at_once_and_the_toast_undo_brings_it_back_intact()
    {
        await OpenReadyAsync();

        await Row("helper.ts").HoverAsync();
        await Row("helper.ts").GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await Expect(Row("helper.ts")).ToHaveCountAsync(0);
        await Expect(Toast).ToContainTextAsync("lib/helper.ts");
        Assert.Empty(_nativeDialogs);

        await Toast.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();

        await Expect(Row("helper.ts")).ToBeVisibleAsync();
        await Expect(Toast).ToBeHiddenAsync();
        await Row("helper.ts").ClickAsync();
        Assert.Equal("export const helper = 42;", await ModelTextAsync("lib/helper.ts"));
    }

    [E2EFact]
    public async Task Tab_in_the_unsaved_confirm_stays_inside_it_and_esc_returns_focus_to_the_editor()
    {
        await OpenReadyAsync();
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();
        await Page.Keyboard.TypeAsync("// my edit");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#unsavedBackdrop")).ToBeVisibleAsync();

        for (int press = 0; press < 5; press++)
        {
            await Page.Keyboard.PressAsync("Tab");
            bool inside = await Page.EvaluateAsync<bool>(
                "() => document.getElementById('unsavedBackdrop').contains(document.activeElement)"
            );
            Assert.True(inside, $"focus left the confirm after Tab press {press + 1}");
        }

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator("#unsavedBackdrop")).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorHost .monaco-editor textarea").First).ToBeFocusedAsync();
    }

    [E2EFact]
    public async Task Deleting_a_saved_version_asks_in_the_page_traps_focus_and_esc_cancels()
    {
        await OpenReadyAsync();
        await Page.EvaluateAsync(
            """
            () => {
                window.__posted = [];
                window.addEventListener('message', (event) => {
                    const type = event.data?.type;
                    if (typeof type === 'string' && type.startsWith('nnz:editor:'))
                        window.__posted.push(type);
                });
                window.postMessage({
                    type: 'nnz:editor:historyPage',
                    payload: { hasMore: false, versions: [
                        { id: 'v2', version: 2, validationStatus: 'valid', isCurrent: true },
                        { id: 'v1', version: 1, validationStatus: 'valid', isCurrent: false },
                    ] },
                }, window.location.origin);
            }
            """
        );
        await Page.EvaluateAsync(
            "() => { document.getElementById('historyActivityItem').hidden = false; }"
        );
        await Page.Locator("#historyActivityItem").ClickAsync();

        await Page.Locator("#historyList .history-row")
            .Last.GetByRole(AriaRole.Button, new() { Name = "Delete" })
            .ClickAsync();

        ILocator dialog = Page.Locator("#confirmBackdrop");
        await Expect(dialog).ToBeVisibleAsync();
        Assert.Empty(_nativeDialogs);
        for (int press = 0; press < 4; press++)
        {
            await Page.Keyboard.PressAsync("Tab");
            Assert.True(
                await Page.EvaluateAsync<bool>(
                    "() => document.getElementById('confirmBackdrop').contains(document.activeElement)"
                ),
                $"focus left the dialog after Tab press {press + 1}"
            );
        }

        await Page.Keyboard.PressAsync("Escape");

        await Expect(dialog).ToBeHiddenAsync();
        await Page.WaitForTimeoutAsync(200);
        string[] posted = await Page.EvaluateAsync<string[]>("() => window.__posted");
        Assert.DoesNotContain("nnz:editor:historyDelete", posted);
        Assert.DoesNotContain("nnz:editor:close", posted);
    }
}
