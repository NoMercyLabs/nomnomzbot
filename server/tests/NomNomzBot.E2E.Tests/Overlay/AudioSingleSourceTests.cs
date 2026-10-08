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
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Overlay;

/// <summary>
/// Owner 2026-10-02: every sound and TTS line plays once, on one page. Two real overlay pages (the Audio
/// Source and the TTS Caption) join the channel's hub; the server fires one TTS line and one sound clip;
/// each page counts what it tried to play. The Audio Source must play both, the other page nothing. With
/// the Audio Source closed, the other page must take over.
/// <para>
/// Safe by construction: output is never audible (the hook mutes media and never calls speechSynthesis), the
/// triggers are the dashboard's own test buttons (no chat message, no moderation, no saved setting), and the
/// test refuses to run while the channel is live. xUnit v2 has no runtime skip, so a live channel fails
/// the test with a message instead of firing anything.
/// </para>
/// </summary>
public sealed class AudioSingleSourceTests : PageTest
{
    // Runs before any page script. Counts every attempt to play audio and silences it.
    private const string CountPlays = """
        (() => {
            window.__nnzPlays = 0;
            const play = HTMLMediaElement.prototype.play;
            HTMLMediaElement.prototype.play = function () {
                window.__nnzPlays++;
                this.muted = true;
                this.volume = 0;
                return play.call(this);
            };
            if (window.speechSynthesis) {
                window.speechSynthesis.speak = () => { window.__nnzPlays++; };
            }
        })();
        """;

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(15);

    [E2EFact]
    public async Task A_tts_line_and_a_sound_play_once_on_the_audio_source_and_never_on_the_other_page()
    {
        await RequireChannelOfflineAsync();
        string? clipId = await FirstEnabledClipIdAsync();
        Assert.True(clipId is not null, "The channel has no enabled sound clip to preview.");

        (string Id, string Url) audio = await WidgetAsync("Audio");
        (string Id, string Url) caption = await WidgetAsync("TTS Caption");

        IPage audioPage = await OpenPageAsync(audio.Url);
        IPage captionPage = await OpenPageAsync(caption.Url);
        await WaitForAttachedAsync(audio.Id, true);
        await WaitForAttachedAsync(caption.Id, true);

        await PostAsync($"/api/v1/channels/{Tenant()}/tts/overlay/test");
        await PostAsync($"/api/v1/sound-clips/{clipId}/preview");

        await WaitForPlaysAsync(audioPage, 2);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(2, await PlaysAsync(audioPage));
        Assert.Equal(0, await PlaysAsync(captionPage));

        await audioPage.CloseAsync();
        await WaitForAttachedAsync(audio.Id, false);
        await PostAsync($"/api/v1/sound-clips/{clipId}/preview");

        await WaitForPlaysAsync(captionPage, 1);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await PlaysAsync(captionPage));
    }

    private async Task<IPage> OpenPageAsync(string url)
    {
        IPage page = await Context.NewPageAsync();
        await page.AddInitScriptAsync(CountPlays);
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        return page;
    }

    private static Task<int> PlaysAsync(IPage page) => page.EvaluateAsync<int>("window.__nnzPlays");

    private static async Task WaitForPlaysAsync(IPage page, int atLeast)
    {
        DateTime deadline = DateTime.UtcNow + Settle;
        while (DateTime.UtcNow < deadline)
        {
            if (await PlaysAsync(page) >= atLeast)
                return;
            await Task.Delay(250);
        }
        Assert.Fail(
            $"The page played {await PlaysAsync(page)} of the expected {atLeast} within {Settle.TotalSeconds} s."
        );
    }

    private async Task WaitForAttachedAsync(string widgetId, bool attached)
    {
        DateTime deadline = DateTime.UtcNow + Settle;
        while (DateTime.UtcNow < deadline)
        {
            JsonElement widget = (await ListWidgetsAsync()).First(w =>
                w.GetProperty("id").GetString() == widgetId
            );
            if (widget.GetProperty("isAttached").GetBoolean() == attached)
                return;
            await Task.Delay(500);
        }
        Assert.Fail(
            $"Widget {widgetId} did not become attached={attached} within {Settle.TotalSeconds} s."
        );
    }

    // Matched on the gallery item's name, which the code owns: a streamer may rename their installed copy.
    private async Task<(string Id, string Url)> WidgetAsync(string galleryName)
    {
        foreach (JsonElement widget in await ListWidgetsAsync())
        {
            if (
                !widget.TryGetProperty("galleryItemId", out JsonElement galleryItem)
                || galleryItem.GetString() is not { } galleryItemId
                || await GalleryNameAsync(galleryItemId) != galleryName
            )
                continue;
            return (
                widget.GetProperty("id").GetString()!,
                widget.GetProperty("overlayUrl").GetString()!
            );
        }
        Assert.Fail($"The channel has no widget installed from the gallery item '{galleryName}'.");
        return default;
    }

    private async Task<string?> GalleryNameAsync(string galleryItemId)
    {
        IAPIResponse item = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/widget-gallery/{galleryItemId}",
            new() { Headers = AuthHeaders() }
        );
        return item.Status == 200
            ? (await item.JsonAsync())!.Value.GetProperty("data").GetProperty("name").GetString()
            : null;
    }

    private async Task<List<JsonElement>> ListWidgetsAsync()
    {
        IAPIResponse list = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{Tenant()}/widgets?take=100",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, list.Status);
        return (await list.JsonAsync())!.Value.GetProperty("data").EnumerateArray().ToList();
    }

    private async Task<string?> FirstEnabledClipIdAsync()
    {
        IAPIResponse list = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/sound-clips?take=100",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, list.Status);
        foreach (
            JsonElement clip in (await list.JsonAsync())!.Value.GetProperty("data").EnumerateArray()
        )
            if (clip.GetProperty("isEnabled").GetBoolean())
                return clip.GetProperty("id").GetString();
        return null;
    }

    private async Task RequireChannelOfflineAsync()
    {
        IAPIResponse stream = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{Tenant()}/stream",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, stream.Status);
        bool live = (await stream.JsonAsync())!
            .Value.GetProperty("data")
            .GetProperty("isLive")
            .GetBoolean();
        Assert.False(
            live,
            "The channel is live. This test plays a sound and a TTS line on the channel's overlay pages, so it refuses to run on a live stream. Run it when offline."
        );
    }

    private async Task PostAsync(string path)
    {
        IAPIResponse response = await Page.APIRequest.PostAsync(
            $"{E2ESettings.BaseUrl}{path}",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, response.Status);
    }

    private static Dictionary<string, string> AuthHeaders() =>
        new() { ["Authorization"] = $"Bearer {E2ESettings.Token}" };

    // The 'tenant' claim of the E2E token: the broadcaster the token acts for.
    private static string Tenant()
    {
        string payload = E2ESettings.Token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return claims.RootElement.GetProperty("tenant").GetString()!;
    }
}
