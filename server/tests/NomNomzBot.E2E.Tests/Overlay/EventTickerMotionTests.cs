// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Overlay;

/// <summary>
/// The Event Ticker as OBS shows it: the channel's own ticker page in a 1920 x 1080 browser, one dashboard test
/// fire, and the chip's place on screen read from the rendered page.
/// </summary>
public sealed class EventTickerMotionTests : PageTest
{
    // The test chip's left edge, and the right edge of the chip in front of it that is already on screen (the
    // chip it must wait behind), or null when the band edge is free.
    private const string TestChipPlace = """
        () => {
            const chips = [...document.querySelectorAll('.nnz-ticker .chip')].map(c => ({
                test: c.textContent.includes('TestFollower'),
                box: c.getBoundingClientRect(),
            }));
            const test = chips.find(c => c.test);
            if (!test) return null;
            const front = chips
                .filter(c => !c.test && c.box.left < 1920 && c.box.left < test.box.left)
                .map(c => c.box.right);
            return [test.box.left, front.length ? Math.max(...front) : null];
        }
        """;

    // The ticker's least gap between two chips (GAP in event_ticker.vue).
    private const double Gap = 18;

    // The test chip waits behind at most one chip on the band edge: a chip of about 330 px at 60 px/s.
    private static readonly TimeSpan ShowsWithin = TimeSpan.FromSeconds(8);

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            ViewportSize = new() { Width = 1920, Height = 1080 },
        };

    // Audit 2026-11-27 on the live ticker: every chip is a theme pill, and the theme zooms pills 1.45x. The chip's
    // own translateX ran inside that zoom, so a chip "at the band edge" was drawn at x 2784, and the test chip
    // queued behind two history chips that had not shown yet. It retired after 10 s, still off screen.
    [E2EFact]
    public async Task A_test_fire_enters_at_the_right_edge_of_the_band_and_shows_at_once()
    {
        await RequireChannelOfflineAsync();
        (string id, string url) = await WidgetAsync("Event Ticker");
        await Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        IAPIResponse fire = await Page.APIRequest.PostAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{Tenant()}/widgets/{id}/test-event",
            new() { Headers = AuthHeaders(), DataObject = new { eventType = "follow" } }
        );
        Assert.Equal(200, fire.Status);

        double? entered = null;
        double allowed = 1920;
        double? shown = null;
        DateTime deadline = DateTime.UtcNow + ShowsWithin;
        while (DateTime.UtcNow < deadline && shown is null)
        {
            double?[]? place = await Page.EvaluateAsync<double?[]?>(TestChipPlace);
            if (place is [double left, _])
            {
                double? front = place[1];
                if (entered is null)
                {
                    entered = left;
                    allowed = Math.Max(1920, (front ?? 0) + Gap);
                }
                if (left < 1920)
                    shown = left;
            }
            await Task.Delay(100);
        }

        Assert.True(entered is not null, "The test chip never reached the ticker page.");
        Assert.True(
            entered <= allowed + 1,
            $"The test chip came in at x {entered:0}; it belongs at x {allowed:0}, the band edge or right behind the chip on it."
        );
        Assert.True(
            shown is not null,
            $"The test chip was not on screen within {ShowsWithin.TotalSeconds} s (it came in at x {entered:0})."
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
            "The channel is live. This test puts a test chip on the channel's ticker, so it refuses to run on a live stream. Run it when offline."
        );
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
