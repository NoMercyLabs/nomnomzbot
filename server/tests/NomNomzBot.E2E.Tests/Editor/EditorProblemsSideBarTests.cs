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
/// The Problems side view lists each error once. A .vue script is checked through a hidden script model whose
/// markers are copied onto the .vue model; the side view must not list the hidden model's copy as a second item.
/// </summary>
public sealed class EditorProblemsSideBarTests : EditorPageTest
{
    [E2EFact]
    public async Task A_vue_type_error_shows_once_in_the_problems_side_view()
    {
        await OpenAsync(
            "vue",
            "App.vue",
            """
            <script setup lang="ts">
            import { ref } from 'vue';
            const count = ref<number>(0);
            count.valeu;
            </script>

            <template>
              <p>{{ count }}</p>
            </template>
            """,
            ""
        );

        await DiagnosticCodesAsync("App.vue", expected: 1);
        await Page.ClickAsync(".activity-item[data-view='problems']");
        await Page.WaitForTimeoutAsync(1_500);

        IReadOnlyList<string> items = await Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#problemsSidebar .hit-where')]
                .map((el) => el.textContent)
            """
        );

        Assert.Equal(["App.vue:4"], items);
        Assert.DoesNotContain(items, item => item.Contains(".__script.ts"));
    }
}
