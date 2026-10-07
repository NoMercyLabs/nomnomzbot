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
/// A preview that fails must say so over the preview, in plain words with file:line, and a click on the message
/// moves the editor to that line (editor-ux-rules R4, R5). The message clears on the next good build. A multi-file
/// Vue widget with sibling imports must render real content, not a black frame.
/// </summary>
public sealed class EditorPreviewErrorTests : EditorPageTest
{
    private const string Index = """
        <script setup lang="ts">
        import Boom from './components/Boom.vue'
        </script>
        <template><Boom /></template>
        """;

    // The throw is on line 4.
    private const string Boom = """
        <script setup lang="ts">
        import { ref } from 'vue'
        const n = ref(1)
        throw new Error('kaboom ' + n.value)
        </script>
        <template><p>never</p></template>
        """;

    private const string ShellIndex = """
        <script setup lang="ts">
        import { ref } from 'vue'
        import Banner from './components/Banner.vue'
        import Badge from './components/Badge.vue'
        const who = ref('kitte')
        </script>
        <template><div><Banner :who="who" /><Badge /></div></template>
        """;

    private const string Banner = """
        <script setup lang="ts">
        defineProps<{ who: string }>()
        </script>
        <template><h1 class="banner">Hello {{ who }}</h1></template>
        """;

    private const string Badge = """
        <script setup lang="ts">
        const label = 'live'
        </script>
        <template><span class="badge">{{ label }}</span></template>
        <style scoped>
        .badge { color: red; }
        </style>
        """;

    [E2EFact]
    public async Task A_component_that_throws_shows_the_message_and_line_and_a_click_moves_the_cursor()
    {
        await OpenAsync(
            "vue",
            "index.vue",
            Index,
            "",
            extraFiles: new() { ["components/Boom.vue"] = Boom }
        );

        ILocator overlay = Page.Locator("#previewError");
        await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(overlay).ToContainTextAsync("kaboom 1");
        await Expect(overlay.Locator(".preview-error-where"))
            .ToHaveTextAsync("components/Boom.vue:4");

        await overlay.Locator(".preview-error-main").ClickAsync();

        await Expect(Page.Locator("#cursor")).ToContainTextAsync("Ln 4,");
        await Expect(Page.Locator(".tab[aria-selected='true']").First)
            .ToContainTextAsync("Boom.vue");
    }

    [E2EFact]
    public async Task A_build_error_shows_over_the_preview_jumps_to_its_line_and_clears_on_the_next_good_build()
    {
        await OpenAsync(
            "vue",
            "index.vue",
            "<script setup lang=\"ts\">\nimport Missing from './nope.vue'\n</script>\n<template><Missing /></template>\n",
            ""
        );

        ILocator overlay = Page.Locator("#previewError");
        await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(overlay).ToContainTextAsync("nope.vue");
        await Expect(overlay.Locator(".preview-error-where")).ToHaveTextAsync("index.vue:2");

        await overlay.Locator(".preview-error-main").ClickAsync();
        await Expect(Page.Locator("#cursor")).ToContainTextAsync("Ln 2,");

        await Page.EvaluateAsync(
            """
            () => window.monaco.editor.getModel(window.monaco.Uri.parse('file:///index.vue'))
                .setValue('<template><p id="fine">fine</p></template>\n<script setup lang="ts">\nconst a = 1\n</script>\n')
            """
        );

        await Expect(overlay).ToBeHiddenAsync(new() { Timeout = 30_000 });
        await Expect(Page.FrameLocator("#previewFrame").Locator("#fine")).ToHaveTextAsync("fine");
    }

    [E2EFact]
    public async Task An_unhandled_promise_rejection_in_the_widget_shows_over_the_preview()
    {
        await OpenAsync(
            "html",
            "index.html",
            "<!doctype html><html><head></head><body><script>Promise.reject(new Error('late failure'));</script></body></html>",
            ""
        );

        await Expect(Page.Locator("#previewError"))
            .ToContainTextAsync("late failure", new() { Timeout = 60_000 });
    }

    [E2EFact]
    public async Task An_optional_chained_subscription_still_shows_its_event_in_the_fire_bar()
    {
        await OpenAsync(
            "html",
            "index.html",
            "<!doctype html><html><head></head><body><script>const nnz = window.NomNomz; nnz?.on?.('reward_redeemed', () => {});</script></body></html>",
            ""
        );

        await Expect(Page.Locator("#fireBar .fire-btn"))
            .ToHaveTextAsync("reward_redeemed", new() { Timeout = 60_000 });
    }

    [E2EFact]
    public async Task A_multi_file_vue_widget_with_sibling_imports_renders_content_and_shows_no_error()
    {
        await OpenAsync(
            "vue",
            "index.vue",
            ShellIndex,
            "",
            extraFiles: new()
            {
                ["components/Banner.vue"] = Banner,
                ["components/Badge.vue"] = Badge,
            }
        );

        IFrameLocator frame = Page.FrameLocator("#previewFrame");
        await Expect(frame.Locator(".banner"))
            .ToHaveTextAsync("Hello kitte", new() { Timeout = 60_000 });
        await Expect(frame.Locator(".badge")).ToHaveTextAsync("live");
        await Expect(Page.Locator("#previewError")).ToBeHiddenAsync();
    }

    // Every script shape a valid SFC can have. Vue's parser drops an empty <script setup>, so it counts as none.
    public static TheoryData<string> EverySfcScriptShape() =>
        new()
        {
            // no script
            "<template><span class=\"logo\">nnz</span></template>\n<style scoped>.logo { color: red; }</style>",
            // an empty script setup
            "<script setup lang=\"ts\"></script>\n<template><span class=\"logo\">nnz</span></template>",
            // an options script
            "<script lang=\"ts\">\nexport default { data: () => ({ word: 'nnz' }) }\n</script>\n<template><span class=\"logo\">{{ word }}</span></template>",
            // a script and a script setup
            "<script lang=\"ts\">\nexport const word = 'nnz'\n</script>\n<script setup lang=\"ts\">\nconst shown = word\n</script>\n<template><span class=\"logo\">{{ shown }}</span></template>",
        };

    [E2ETheory]
    [MemberData(nameof(EverySfcScriptShape))]
    public async Task A_child_component_of_every_script_shape_renders_in_the_preview(string logo)
    {
        await OpenAsync(
            "vue",
            "index.vue",
            "<script setup lang=\"ts\">\nimport Logo from './components/Logo.vue'\n</script>\n<template><Logo /></template>\n",
            "",
            extraFiles: new() { ["components/Logo.vue"] = logo }
        );

        await Expect(Page.FrameLocator("#previewFrame").Locator(".logo"))
            .ToHaveTextAsync("nnz", new() { Timeout = 60_000 });
        await Expect(Page.Locator("#previewError")).ToBeHiddenAsync();
    }
}
