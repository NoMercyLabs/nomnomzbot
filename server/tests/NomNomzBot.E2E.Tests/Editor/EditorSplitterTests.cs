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

public sealed class EditorSplitterTests : EditorPageTest
{
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p></body></html>
        """;

    [E2EFact]
    public async Task Dragging_the_split_over_the_preview_grows_the_editor_and_the_release_ends_the_drag()
    {
        await OpenReadyAsync();
        LocatorBoundingBoxResult split = (await Page.Locator("#splitter").BoundingBoxAsync())!;
        float x = split.X + split.Width / 2;
        float y = split.Y + split.Height / 2;
        float before = await CodeWidthAsync();

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        // To the right is over the preview iframe, which used to swallow the move and the release.
        await Page.Mouse.MoveAsync(x + 200, y, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        float grown = await CodeWidthAsync();
        await Page.Mouse.MoveAsync(x + 260, y, new() { Steps = 5 });

        Assert.InRange(grown - before, 190, 210);
        Assert.Equal(grown, await CodeWidthAsync());
    }

    [E2EFact]
    public async Task Dragging_the_split_back_shrinks_the_editor_again()
    {
        await OpenReadyAsync();
        LocatorBoundingBoxResult split = (await Page.Locator("#splitter").BoundingBoxAsync())!;
        float x = split.X + split.Width / 2;
        float y = split.Y + split.Height / 2;
        float before = await CodeWidthAsync();

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x + 150, y, new() { Steps = 10 });
        await Page.Mouse.MoveAsync(x - 100, y, new() { Steps = 10 });
        await Page.Mouse.UpAsync();

        Assert.InRange(before - await CodeWidthAsync(), 90, 110);
    }

    // The preview frame loads before Monaco; the shell, and with it the handle, shows only after Monaco.
    private async Task OpenReadyAsync()
    {
        await OpenAsync("html", "index.html", Widget, sdkTypes: "");
        await Expect(Page.FrameLocator("#previewFrame").Locator("#ready"))
            .ToHaveTextAsync("ready", new() { Timeout = 60_000 });
        await Expect(Page.Locator("#splitter")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private Task<float> CodeWidthAsync() =>
        Page.EvaluateAsync<float>(
            "() => document.querySelector('main.code').getBoundingClientRect().width"
        );
}
