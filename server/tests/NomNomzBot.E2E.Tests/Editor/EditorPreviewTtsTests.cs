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
/// A widget in the editor preview that asks for <c>tts_synthesize</c> really speaks: the request goes through the
/// host bridge (<c>nnz:editor:previewAction</c>), and the audio URL of the host's answer is played by the preview
/// frame. A failed or unanswered request is a visible console error, never silence. The host is stubbed here: the
/// test page is its own parent, so it sees the page's request and answers it the way the Compose host does.
/// </summary>
public sealed class EditorPreviewTtsTests : EditorPageTest
{
    private const string AudioUrl = "data:audio/wav;base64,UklGRg==";

    // Audio is replaced by a recorder so the test reads what the frame tried to play; the frame is sandboxed, so the
    // widget reports through the console. The timeout is shortened by the widget through the SDK's test hook.
    private const string Widget = """
        <!doctype html>
        <html><head></head><body><p id="ready">ready</p>
        <script>
        window.Audio = function (src) {
          console.log('audio src=' + src);
          this.play = function () { console.log('audio play'); return Promise.resolve(); };
        };
        window.__nnzPreviewActionTimeoutMs = 600;
        window.NomNomz.actions.invoke('tts_synthesize', { text: 'hello chat' }, { user: 'kitte' })
          .then(function (r) { console.log('result success=' + r.success + ' code=' + r.errorCode); });
        </script></body></html>
        """;

    [E2EFact]
    public async Task A_successful_host_reply_plays_the_audio_url_and_sends_the_request_shape()
    {
        await OpenWidgetAsync(Widget, "success");

        await Expect(Page.Locator("#consoleList .console-text"))
            .ToContainTextAsync(
                [$"audio src={AudioUrl}", "audio play", "result success=true code=null"],
                new() { Timeout = 60_000 }
            );
        string request = await Page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__previewActions)"
        );
        Assert.Contains("\"actionType\":\"tts_synthesize\"", request);
        Assert.Contains("\"params\":{\"text\":\"hello chat\"}", request);
        Assert.Contains("\"variables\":{\"user\":\"kitte\"}", request);
    }

    [E2EFact]
    public async Task A_failed_host_reply_is_a_console_error_naming_the_code()
    {
        await OpenWidgetAsync(Widget, "error");

        ILocator row = Page.Locator("#consoleList .console-row[data-level='error']");
        await Expect(row).ToContainTextAsync("TTS_NOT_CONFIGURED", new() { Timeout = 60_000 });
        await Expect(Page.Locator("#consoleList .console-text"))
            .Not.ToContainTextAsync(["audio src="]);
        await Expect(Page.Locator("#consoleList .console-text"))
            .ToContainTextAsync(["result success=false code=TTS_NOT_CONFIGURED"]);
    }

    [E2EFact]
    public async Task No_host_reply_times_out_with_a_console_error()
    {
        await OpenWidgetAsync(Widget, "silent");

        ILocator row = Page.Locator("#consoleList .console-row[data-level='error']");
        await Expect(row).ToContainTextAsync("PREVIEW_ACTION_TIMEOUT", new() { Timeout = 60_000 });
        await Expect(Page.Locator("#consoleList .console-text"))
            .ToContainTextAsync(["result success=false code=PREVIEW_ACTION_TIMEOUT"]);
    }

    private async Task OpenWidgetAsync(string source, string hostBehaviour)
    {
        await ServeEditorFromTheWorkingTreeAsync();
        await Page.GotoAsync(
            $"{E2ESettings.BaseUrl}/editor/index.html",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded }
        );
        await Page.EvaluateAsync(
            """
            (behaviour) => {
                window.__previewActions = [];
                window.addEventListener('message', (event) => {
                    const data = event.data;
                    if (data?.type !== 'nnz:editor:previewAction') return;
                    window.__previewActions.push(data);
                    if (behaviour === 'silent') return;
                    const reply = behaviour === 'success'
                        ? { success: true, output: null, error: null, errorCode: null,
                            variables: { 'tts.audioUrl': 'data:audio/wav;base64,UklGRg==' } }
                        : { success: false, output: null, error: 'TTS is not configured.',
                            errorCode: 'TTS_NOT_CONFIGURED', variables: {} };
                    window.postMessage(
                        { type: 'nnz:editor:previewActionResult', requestId: data.requestId, ...reply },
                        window.location.origin);
                });
            }
            """,
            hostBehaviour
        );
        await Page.EvaluateAsync(
            """
            (source) => window.postMessage({
                type: 'nnz:editor:open',
                payload: {
                    title: 'Tts',
                    language: 'html',
                    entry: 'index.html',
                    files: { 'index.html': source },
                    sdkTypes: '',
                    fireSamples: {},
                    eventSubscriptions: [],
                    widget: { id: 'w-1', name: 'Speaker', settings: {} },
                },
            }, window.location.origin)
            """,
            source
        );
        await Page.Locator(".activity-item[data-view='run']").ClickAsync();
        await Expect(Page.FrameLocator("#previewFrame").Locator("#ready"))
            .ToHaveTextAsync("ready", new() { Timeout = 60_000 });
    }
}
