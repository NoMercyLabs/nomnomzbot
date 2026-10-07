// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// The fire bar of the widget editor preview (owner 2026-10-07: test events must be reliable): every event of the
/// catalogue can be fired, a sample can be edited before it is fired, bad JSON cannot be fired, and a search narrows
/// the list. The widget records everything it receives, so each test reads what the widget actually got.
/// </summary>
public sealed class EditorFireBarTests : EditorPageTest
{
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p><ol id="got"></ol>
        <script>
        NomNomz.onAny(function (type, data) {
            var li = document.createElement('li');
            li.dataset.type = type;
            li.textContent = JSON.stringify(data);
            document.getElementById('got').appendChild(li);
        });
        </script></body></html>
        """;

    [E2EFact]
    public async Task Every_catalogue_event_can_be_fired_and_the_widget_receives_its_sample()
    {
        List<string> types = CatalogueEventNames();
        Assert.True(types.Count >= 40, $"The catalogue parse found only {types.Count} events.");
        Dictionary<string, object> samples = types.ToDictionary(
            type => type,
            type => (object)new { marker = "sample-of-" + type }
        );
        await OpenWidgetAsync(samples, declared: []);

        foreach (string type in types)
        {
            await FireButton(type).ClickAsync();

            await Expect(Received(type)).ToHaveCountAsync(1);
            await Expect(Received(type)).ToHaveTextAsync($"{{\"marker\":\"sample-of-{type}\"}}");
        }
    }

    [E2EFact]
    public async Task Edit_opens_the_sample_as_a_formatted_json_tab_in_the_main_editor()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte", total = 3 } },
            declared: ["follow"]
        );

        await EditButton("follow").ClickAsync();

        await Expect(EventTab("follow")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal("json", await ActiveModelAsync("getLanguageId()"));
        Assert.Equal("file:///events/follow.json", await ActiveModelAsync("uri.toString()"));
        Assert.Equal(
            "{\n  \"user_name\": \"kitte\",\n  \"total\": 3\n}",
            await ActiveModelAsync("getValue()")
        );
    }

    [E2EFact]
    public async Task Fire_sends_the_edited_data_from_the_button_and_from_ctrl_enter()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte" } },
            declared: ["follow"]
        );
        await EditButton("follow").ClickAsync();

        await SetActiveTextAsync("{\"user_name\":\"edited\",\"extra\":7}");
        await Page.Locator("#eventFire").ClickAsync();

        await Expect(Received("follow")).ToHaveTextAsync("{\"user_name\":\"edited\",\"extra\":7}");
        await Expect(Page.Locator("#previewLog li")).ToHaveTextAsync(["Fired follow"]);

        await SetActiveTextAsync("{\"user_name\":\"second\"}");
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();
        await Page.Keyboard.PressAsync("Control+Enter");

        await Expect(Received("follow")).ToHaveCountAsync(2);
        await Expect(Received("follow").Last).ToHaveTextAsync("{\"user_name\":\"second\"}");
    }

    [E2EFact]
    public async Task Invalid_json_disables_fire_and_lands_in_the_problems_panel_until_it_is_fixed()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte" } },
            declared: ["follow"]
        );
        await EditButton("follow").ClickAsync();

        await SetActiveTextAsync("{\"user_name\": ");

        await Expect(Page.Locator("#eventFire")).ToBeDisabledAsync();
        await Expect(Page.Locator("#problems .problem").First)
            .ToContainTextAsync("events/follow.json");
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();
        await Page.Keyboard.PressAsync("Control+Enter");
        await Expect(Received("follow")).ToHaveCountAsync(0);

        await SetActiveTextAsync("{\"user_name\":\"fixed\"}");

        await Expect(Page.Locator("#eventFire")).ToBeEnabledAsync();
        await Expect(Page.Locator("#problems .problem")).ToHaveCountAsync(0);
    }

    [E2EFact]
    public async Task A_wrong_type_or_an_unknown_key_is_underlined_from_the_samples_own_shape()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte", total = 3 } },
            declared: ["follow"]
        );
        await EditButton("follow").ClickAsync();

        await SetActiveTextAsync("{\"user_name\":5,\"total\":3,\"bogus\":1}");

        await Expect(Page.Locator("#problems .problem")).ToHaveCountAsync(2);
        await Expect(Page.Locator("#problems")).ToContainTextAsync("bogus");
        await Expect(Page.Locator("#problems")).ToContainTextAsync("string");
        // Schema problems are advice: the author may still fire data of another shape.
        await Expect(Page.Locator("#eventFire")).ToBeEnabledAsync();
    }

    [E2EFact]
    public async Task The_event_tab_never_reaches_the_save_payload_or_marks_the_widget_changed()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte" } },
            declared: ["follow"]
        );
        await Page.EvaluateAsync(
            """
            () => {
                window.__saves = [];
                window.__closes = 0;
                window.addEventListener('message', (event) => {
                    if (event.data?.type === 'nnz:editor:save') window.__saves.push(Object.keys(event.data.files));
                    if (event.data?.type === 'nnz:editor:close') window.__closes++;
                });
            }
            """
        );
        await EditButton("follow").ClickAsync();
        await SetActiveTextAsync("{\"user_name\":\"edited\"}");
        await Page.Locator("#editorHost .monaco-editor textarea").First.FocusAsync();

        await Page.Keyboard.PressAsync("Control+s");
        await Page.WaitForTimeoutAsync(300);

        string[][] saves = await Page.EvaluateAsync<string[][]>("() => window.__saves");
        Assert.Single(saves);
        Assert.Equal(["index.html"], saves[0]);

        await Page.Locator("#eventClose").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForTimeoutAsync(300);
        await Expect(Page.Locator("#unsavedBackdrop")).ToBeHiddenAsync();
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__closes"));
    }

    private ILocator EventTab(string type) =>
        Page.Locator($"#tabs .tab[title='events/{type}.json']");

    private Task<string> ActiveModelAsync(string member) =>
        Page.EvaluateAsync<string>($"() => monaco.editor.getEditors()[0].getModel().{member}");

    private async Task SetActiveTextAsync(string text)
    {
        await Page.EvaluateAsync(
            "(text) => monaco.editor.getEditors()[0].getModel().setValue(text)",
            text
        );
        // Markers come from the JSON worker a moment after the text changes.
        await Page.WaitForTimeoutAsync(800);
    }

    [E2EFact]
    public async Task A_failed_sample_fetch_is_shown_and_no_event_can_be_fired()
    {
        await OpenWidgetAsync(
            new(),
            declared: ["follow"],
            samplesError: "The test events could not be loaded."
        );

        await Expect(Page.Locator("#fireSamplesError"))
            .ToHaveTextAsync(new Regex("^The test events could not be loaded"));
        await Expect(FireButton("follow")).ToBeDisabledAsync();
        await Expect(EditButton("follow")).ToBeDisabledAsync();
        await FireButton("follow").ClickAsync(new() { Force = true });
        await Expect(Received("follow")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#previewLog li")).ToHaveCountAsync(0);
    }

    [E2EFact]
    public async Task Search_narrows_the_list_and_declared_events_come_first()
    {
        await OpenWidgetAsync(
            new()
            {
                ["cheer"] = new { bits = 1 },
                ["follow"] = new { user_name = "a" },
                ["raid"] = new { viewers = 3 },
                ["zzz_declared"] = new { },
                ["_default"] = new { },
            },
            declared: ["zzz_declared"]
        );

        await Expect(Page.Locator("#fireBar .fire-btn"))
            .ToHaveTextAsync(["zzz_declared", "cheer", "follow", "raid"]);

        await Page.Locator("#fireSearch").FillAsync("rai");

        await Expect(Page.Locator("#fireBar .fire-btn:visible")).ToHaveTextAsync(["raid"]);
    }

    private const string FilteringWidget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p><p id="hit"></p>
        <script>
        NomNomz.on('reward_redeemed', function (event) {
            if (event.rewardId !== window.WIDGET_SETTINGS.rewardId) return;
            document.getElementById('hit').textContent = 'matched ' + event.rewardId;
        });
        </script></body></html>
        """;

    [E2EFact]
    public async Task A_fired_sample_carries_the_reward_id_of_the_widgets_own_setting()
    {
        await OpenWidgetAsync(
            new() { ["reward_redeemed"] = new { rewardId = "test-reward", user_name = "kitte" } },
            declared: ["reward_redeemed"],
            source: FilteringWidget,
            settings: new { rewardId = "abc" }
        );

        await FireButton("reward_redeemed").ClickAsync();

        await Expect(Page.FrameLocator("#previewFrame").Locator("#hit"))
            .ToHaveTextAsync("matched abc");
        await EditButton("reward_redeemed").ClickAsync();
        Assert.Equal(
            "{\n  \"rewardId\": \"abc\",\n  \"user_name\": \"kitte\"\n}",
            await ActiveModelAsync("getValue()")
        );
    }

    [E2EFact]
    public async Task A_reward_id_edited_in_the_event_tab_wins_over_the_setting()
    {
        await OpenWidgetAsync(
            new() { ["reward_redeemed"] = new { rewardId = "test-reward" } },
            declared: ["reward_redeemed"],
            source: FilteringWidget,
            settings: new { rewardId = "abc" }
        );
        await EditButton("reward_redeemed").ClickAsync();

        await SetActiveTextAsync("{\"rewardId\":\"typed\"}");
        await Page.Locator("#eventFire").ClickAsync();

        await Expect(Page.FrameLocator("#previewFrame").Locator("#hit")).ToHaveCountAsync(1);
        await Expect(Page.FrameLocator("#previewFrame").Locator("#hit")).ToHaveTextAsync("");
    }

    private ILocator FireButton(string type) =>
        Page.Locator($"#fireBar .fire-row[data-type='{type}'] .fire-btn");

    private ILocator EditButton(string type) =>
        Page.Locator($"#fireBar .fire-row[data-type='{type}'] .fire-edit");

    private ILocator Received(string type) =>
        Page.FrameLocator("#previewFrame").Locator($"#got li[data-type='{type}']");

    private async Task OpenWidgetAsync(
        Dictionary<string, object> samples,
        string[] declared,
        string? samplesError = null,
        string source = Widget,
        object? settings = null
    )
    {
        await ServeEditorFromTheWorkingTreeAsync();
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            ([source, samples, declared, samplesError, settings]) => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Fire bar',
                    language: 'html',
                    entry: 'index.html',
                    files: { 'index.html': source },
                    sdkTypes: '',
                    fireSamples: samples,
                    fireSamplesError: samplesError,
                    eventSubscriptions: declared,
                    widget: { id: 'w-1', name: 'Alerts', settings },
                },
            }, window.location.origin)
            """,
            new object?[] { source, samples, declared, samplesError, settings ?? new { } }
        );
        await Page.Locator(".activity-item[data-view='run']").ClickAsync();
        // The frame must be rendered with the SDK before a fired event has a handler to reach.
        await Expect(Page.FrameLocator("#previewFrame").Locator("#ready"))
            .ToHaveTextAsync("ready", new() { Timeout = 60_000 });
    }

    // Every event name of the server's widget event table, read from the source so a new event is covered at once.
    private static List<string> CatalogueEventNames()
    {
        string registry = Path.GetFullPath(
            Path.Combine(
                EditorAssetsFolder(),
                "..",
                "..",
                "Hubs",
                "Broadcasters",
                "WidgetEventPayloadRegistry.cs"
            )
        );
        return Regex
            .Matches(File.ReadAllText(registry), "new\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
