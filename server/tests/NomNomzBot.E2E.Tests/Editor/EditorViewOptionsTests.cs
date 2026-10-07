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
/// The minimap and word-wrap toggles are the viewer's own choice (owner 2026-10-07: "make the editor remember the
/// state of the minimap so i don't have to turn it off every time the editor opens"): the next open of the editor
/// in the same browser starts the way the viewer left it, in the editor and on the toggle labels.
/// </summary>
public sealed class EditorViewOptionsTests : EditorPageTest
{
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p></body></html>
        """;

    [E2EFact]
    public async Task The_minimap_and_wrap_toggles_are_remembered_on_the_next_open()
    {
        await OpenReadyAsync();
        await Expect(Page.Locator("#minimap")).ToHaveTextAsync("Minimap: on");
        await Expect(Page.Locator("#wrap")).ToHaveTextAsync("Wrap: off");

        await Page.Locator("#minimap").ClickAsync();
        await Page.Locator("#wrap").ClickAsync();
        await OpenReadyAsync();

        await Expect(Page.Locator("#minimap")).ToHaveTextAsync("Minimap: off");
        await Expect(Page.Locator("#wrap")).ToHaveTextAsync("Wrap: on");
        Assert.False(
            await Page.EvaluateAsync<bool>(
                "() => window.monaco.editor.getEditors()[0].getOption(window.monaco.editor.EditorOption.minimap).enabled"
            )
        );
        Assert.Equal(
            "on",
            await Page.EvaluateAsync<string>(
                "() => window.monaco.editor.getEditors()[0].getOption(window.monaco.editor.EditorOption.wordWrap)"
            )
        );
    }

    [E2EFact]
    public async Task Turning_the_minimap_back_on_is_remembered_too()
    {
        await OpenReadyAsync();
        await Page.Locator("#minimap").ClickAsync();
        await OpenReadyAsync();
        await Page.Locator("#minimap").ClickAsync();
        await OpenReadyAsync();

        await Expect(Page.Locator("#minimap")).ToHaveTextAsync("Minimap: on");
        Assert.True(
            await Page.EvaluateAsync<bool>(
                "() => window.monaco.editor.getEditors()[0].getOption(window.monaco.editor.EditorOption.minimap).enabled"
            )
        );
    }

    // The shell, and with it the status-bar toggles, shows only after Monaco has booted.
    private async Task OpenReadyAsync()
    {
        await OpenAsync("html", "index.html", Widget, sdkTypes: "");
        await Expect(Page.FrameLocator("#previewFrame").Locator("#ready"))
            .ToHaveTextAsync("ready", new() { Timeout = 60_000 });
        await Expect(Page.Locator("#minimap")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
