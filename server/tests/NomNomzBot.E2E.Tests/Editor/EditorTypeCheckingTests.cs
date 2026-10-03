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
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The code editor must catch every type error before the code runs (owner 2026-10-02: "100% type safe").
/// These open the real editor page, hand it a project the way the dashboard does, and read the diagnostics
/// Monaco's TypeScript worker reports. A script runs in the Jint sandbox, which has no DOM; a widget runs in a
/// browser, and its plain JavaScript is checked as strictly as TypeScript.
/// </summary>
public sealed class EditorTypeCheckingTests : EditorPageTest
{
    private const string ScriptTypes = "declare const bot: { args: string[] };";

    [E2EFact]
    public async Task A_script_is_checked_strictly()
    {
        await OpenAsync(
            "script",
            "index.ts",
            """
            const first = bot.args[0];
            first.length;
            function shout(text) { return text; }
            document.title;
            """,
            ScriptTypes
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.ts", expected: 3);

        // An argument the trigger did not pass is undefined.
        Assert.Contains("18048", codes);
        // A parameter without a type is an implicit any.
        Assert.Contains("7006", codes);
        // A script runs in the sandbox, where there is no DOM.
        Assert.Contains("2584", codes);
    }

    [E2EFact]
    public async Task A_widget_s_plain_javascript_is_type_checked_and_has_the_dom()
    {
        await OpenAsync(
            "vanilla-js",
            "index.js",
            """
            const count = 1;
            count.toUpperCase();
            document.title.toUpperCase();
            """,
            "declare const NomNomz: { reportError(message: string): void };"
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.js", expected: 1);

        // A number has no toUpperCase; a widget page does have a document.
        Assert.Equal(["2339"], codes);
    }

    [E2EFact]
    public async Task A_vue_script_setup_typo_is_flagged_on_its_line_of_the_vue_file()
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

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("App.vue", expected: 1);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("App.vue");

        // Property 'valeu' does not exist on Ref<number>: TS2339, or TS2551 when TypeScript suggests 'value'.
        // The line is the line of the .vue file itself, and the hidden script model never shows up as a file.
        Assert.All(codes, code => Assert.Contains(code, new[] { "2339", "2551" }));
        Assert.Equal([4], lines);
        Assert.Equal(["App.vue"], await ProblemFilesAsync());
    }

    [E2EFact]
    public async Task A_correct_vue_script_setup_has_no_problems()
    {
        await OpenAsync(
            "vue",
            "App.vue",
            """
            <script setup lang="ts">
            import { computed, ref } from 'vue';
            const count = ref<number>(0);
            const double = computed<number>(() => count.value * 2);
            function bump(): void {
              count.value += 1;
            }
            </script>

            <template>
              <button @click="bump">{{ count }} {{ double }}</button>
            </template>
            """,
            ""
        );

        Assert.Equal(0, await WorkerDiagnosticCountAsync("App.vue.__script.ts"));
        Assert.Empty(await DiagnosticLinesAsync("App.vue"));
    }

    [E2EFact]
    public async Task A_react_widget_flags_a_wrong_state_setter_argument_and_a_wrong_event_property()
    {
        await OpenAsync(
            "react",
            "index.tsx",
            """
            import { useState } from 'react';
            export function Counter() {
              const [count, setCount] = useState<number>(0);
              setCount("x");
              return <div onClick={e => e.foo}>{count}</div>;
            }
            """,
            ""
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.tsx", expected: 2);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.tsx");

        // A string is not a number (2345); a click event has no 'foo' (2339).
        Assert.Contains("2345", codes);
        Assert.Contains("2339", codes);
        Assert.Equal([4, 5], lines.Order());
    }

    [E2EFact]
    public async Task A_correct_react_widget_has_no_problems()
    {
        await OpenAsync(
            "react",
            "index.tsx",
            """
            import React, { useState } from 'react';
            export function Counter() {
              const [count, setCount] = useState<number>(0);
              const [label, setLabel] = React.useState<string>('n');
              return (
                <button className="counter" onClick={e => { setCount(count + e.detail); setLabel(label + 'x'); }}>
                  {count}
                </button>
              );
            }
            """,
            ""
        );

        Assert.Equal(0, await WorkerDiagnosticCountAsync("index.tsx"));
        Assert.Empty(await DiagnosticLinesAsync("index.tsx"));
    }

    [E2EFact]
    public async Task The_notice_shows_only_when_the_host_says_the_sdk_types_did_not_load()
    {
        await OpenAsync("vanilla-js", "index.js", "const a = 1;", "", sdkTypesUnavailable: true);
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sdkTypesNotice")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sdkTypesNotice"))
            .ToContainTextAsync(
                "SDK types could not load. Type checking is off until you reopen this file."
            );

        await OpenAsync("vanilla-js", "index.js", "const a = 1;", "");
        await Expect(Page.Locator("#shell")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sdkTypesNotice")).ToBeHiddenAsync();
    }

    [E2EFact]
    public async Task A_command_script_flags_a_variable_the_command_never_sets()
    {
        IAPIResponse types = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/sdk/types.d.ts?context=script&trigger=command",
            new()
            {
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {E2ESettings.Token}",
                },
            }
        );
        Assert.Equal(200, types.Status);

        await OpenAsync(
            "script",
            "index.ts",
            """
            bot.getVar('user.name');
            bot.getVar('typo');
            bot.getVar('typo', true);
            bot.getVar('args.2');
            """,
            await types.TextAsync()
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.ts", expected: 1);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.ts");

        // Only the unknown key is an error: a key the command sets, a key read with the dynamic flag, and a
        // positional argument all type-check.
        Assert.Single(codes);
        Assert.Equal([2], lines);
    }

    [E2EFact]
    public async Task A_script_s_action_invoke_needs_a_real_action_type_and_its_required_params()
    {
        await OpenAsync(
            "script",
            "index.ts",
            """
            nnz.api.actions.invoke('obs_switch_scene', { scene: 'Main' });
            nnz.api.actions.invoke('obs_switch_scene', {});
            nnz.api.actions.invoke('obs_swich_scene', { scene: 'Main' });
            nnz.api.actions.invoke('music_next');
            """,
            await SdkTypesAsync("script")
        );

        await DiagnosticCodesAsync("index.ts", expected: 2);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.ts");

        // A missing required param and an unknown action type are errors; a full call and a call to an
        // action without required params type-check.
        Assert.Equal([2, 3], lines.Order());
    }

    [E2EFact]
    public async Task A_widget_s_action_invoke_needs_a_real_action_type_and_its_required_params()
    {
        await OpenAsync(
            "vanilla-js",
            "index.js",
            """
            NomNomz.actions.invoke('obs_switch_scene', { scene: 'Main' });
            NomNomz.actions.invoke('obs_switch_scene', {});
            NomNomz.actions.invoke('obs_swich_scene', { scene: 'Main' });
            NomNomz.actions.invoke('music_next');
            """,
            await SdkTypesAsync("widget")
        );

        await DiagnosticCodesAsync("index.js", expected: 2);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.js");

        Assert.Equal([2, 3], lines.Order());
    }

    [E2EFact]
    public async Task A_widget_event_handler_flags_a_misspelled_payload_field()
    {
        await OpenAsync(
            "vanilla-js",
            "index.js",
            """
            NomNomz.on('follow', d => console.log(d.displayName));
            NomNomz.on('follow', d => console.log(d.displayNme));
            NomNomz.on('custom.heartrate', d => console.log(d.fields.bpm));
            NomNomz.on('custom.heartrate', d => console.log(d.feilds));
            """,
            await SdkTypesAsync("widget")
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.js", expected: 2);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.js");

        // Property 'displayNme' / 'feilds' does not exist: TS2339, or TS2551 when TypeScript also suggests the
        // real name. The correctly spelled fields type-check.
        Assert.All(codes, code => Assert.Contains(code, new[] { "2339", "2551" }));
        Assert.Equal([2, 4], lines.Order());
    }

    [E2EFact]
    public async Task A_widget_script_reading_a_misspelled_setting_is_an_error()
    {
        (string widgetTypes, string settingKey) = await WidgetWithTypedSettingsAsync();

        await OpenAsync(
            "vanilla-js",
            "index.js",
            $"""
            console.log(NomNomz.settings.{settingKey});
            console.log(NomNomz.settings.{settingKey}Typo);
            """,
            widgetTypes
        );

        IReadOnlyList<string> codes = await DiagnosticCodesAsync("index.js", expected: 1);
        IReadOnlyList<int> lines = await DiagnosticLinesAsync("index.js");

        // The real setting type-checks; the misspelled one does not exist on NnzWidgetSettings.
        Assert.All(codes, code => Assert.Contains(code, new[] { "2339", "2551" }));
        Assert.Equal([2], lines);
    }

    // The first of this channel's widgets whose types carry a settings interface with at least one member, and the
    // name of that member. The channel is the token's own tenant, so the test never names a widget id.
    private async Task<(string Types, string SettingKey)> WidgetWithTypedSettingsAsync()
    {
        IAPIResponse list = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{TokenTenant()}/widgets?take=100",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, list.Status);
        JsonElement page = (await list.JsonAsync())!.Value;

        foreach (JsonElement widget in page.GetProperty("data").EnumerateArray())
        {
            string id = widget.GetProperty("id").GetString()!;
            IAPIResponse types = await Page.APIRequest.GetAsync(
                $"{E2ESettings.BaseUrl}/api/v1/sdk/types.d.ts?context=widget&widget={id}",
                new() { Headers = AuthHeaders() }
            );
            Assert.Equal(200, types.Status);
            string text = await types.TextAsync();
            Match member = Regex.Match(
                text,
                @"interface NnzWidgetSettings \{[^}]*?^\s*(?:readonly\s+)?([A-Za-z_]\w*)\??:",
                RegexOptions.Multiline
            );
            if (member.Success)
                return (text, member.Groups[1].Value);
        }

        Assert.Fail("No widget on this channel has a settings schema with a plain-named setting.");
        return default;
    }

    private async Task<string> SdkTypesAsync(string context)
    {
        IAPIResponse types = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/sdk/types.d.ts?context={context}",
            new()
            {
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {E2ESettings.Token}",
                },
            }
        );
        Assert.Equal(200, types.Status);
        return await types.TextAsync();
    }
}
