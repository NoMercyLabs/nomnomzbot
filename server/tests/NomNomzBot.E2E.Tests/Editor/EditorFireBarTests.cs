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
    public async Task An_edited_sample_is_what_the_widget_receives()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte" } },
            declared: ["follow"]
        );

        await EditButton("follow").ClickAsync();
        await Expect(Page.Locator("#fireJson"))
            .ToHaveValueAsync(new Regex("\"user_name\":\\s*\"kitte\""));
        await Page.Locator("#fireJson").FillAsync("{\"user_name\":\"edited\",\"extra\":7}");
        await Page.Locator("#fireSend").ClickAsync();

        await Expect(Received("follow")).ToHaveTextAsync("{\"user_name\":\"edited\",\"extra\":7}");
        await Expect(Page.Locator("#previewLog li")).ToHaveTextAsync(["Fired follow"]);
    }

    [E2EFact]
    public async Task Invalid_json_shows_an_error_and_disables_fire_until_it_is_fixed()
    {
        await OpenWidgetAsync(
            new() { ["follow"] = new { user_name = "kitte" } },
            declared: ["follow"]
        );

        await EditButton("follow").ClickAsync();
        await Page.Locator("#fireJson").FillAsync("{\"user_name\": ");

        await Expect(Page.Locator("#fireJsonError")).ToBeVisibleAsync();
        await Expect(Page.Locator("#fireSend")).ToBeDisabledAsync();
        await Expect(Received("follow")).ToHaveCountAsync(0);

        await Page.Locator("#fireJson").FillAsync("{\"user_name\":\"fixed\"}");

        await Expect(Page.Locator("#fireJsonError")).ToBeHiddenAsync();
        await Expect(Page.Locator("#fireSend")).ToBeEnabledAsync();
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

    private ILocator FireButton(string type) =>
        Page.Locator($"#fireBar .fire-row[data-type='{type}'] .fire-btn");

    private ILocator EditButton(string type) =>
        Page.Locator($"#fireBar .fire-row[data-type='{type}'] .fire-edit");

    private ILocator Received(string type) =>
        Page.FrameLocator("#previewFrame").Locator($"#got li[data-type='{type}']");

    private async Task OpenWidgetAsync(
        Dictionary<string, object> samples,
        string[] declared,
        string? samplesError = null
    )
    {
        await ServeEditorFromTheWorkingTreeAsync();
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            ([source, samples, declared, samplesError]) => window.postMessage({
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
                    widget: { id: 'w-1', name: 'Alerts', settings: {} },
                },
            }, window.location.origin)
            """,
            new object?[] { Widget, samples, declared, samplesError }
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
