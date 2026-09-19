// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// S-YT-STICKER-IMAGE — proves <see cref="YouTubeSuperStickerImageResolver"/> parses Google's real
/// <c>sticker_ids_to_urls.csv</c> line shape (<c>sticker_id,url</c>, no header) and degrades gracefully —
/// an unknown id resolves to null, and a fetch failure resolves to null — never throwing either way, since
/// a sticker image is decoration and must never block chat delivery.
/// </summary>
public sealed class YouTubeSuperStickerImageResolverTests
{
    // A real sample of the CSV's shape (confirmed live 2026-09-19): "id,url" per line, no header row.
    private const string SampleCsv =
        "biggest_fans_blush_v2,https://lh3.googleusercontent.com/biggest_fans_blush_v2.png\n"
        + "biggest_fans_heart_v2,https://lh3.googleusercontent.com/biggest_fans_heart_v2.png\n";

    [Fact]
    public async Task A_known_sticker_id_resolves_to_its_real_image_url()
    {
        YouTubeSuperStickerImageResolver resolver = Build(
            new RecordingHandler(HttpStatusCode.OK, SampleCsv)
        );

        string? url = await resolver.ResolveAsync("biggest_fans_heart_v2");

        url.Should().Be("https://lh3.googleusercontent.com/biggest_fans_heart_v2.png");
    }

    [Fact]
    public async Task An_unknown_sticker_id_resolves_to_null_without_throwing()
    {
        YouTubeSuperStickerImageResolver resolver = Build(
            new RecordingHandler(HttpStatusCode.OK, SampleCsv)
        );

        string? url = await resolver.ResolveAsync("not-a-real-sticker-id");

        url.Should().BeNull();
    }

    [Fact]
    public async Task A_fetch_failure_resolves_to_null_without_throwing()
    {
        YouTubeSuperStickerImageResolver resolver = Build(
            new RecordingHandler(HttpStatusCode.ServiceUnavailable, string.Empty)
        );

        string? url = await resolver.ResolveAsync("biggest_fans_heart_v2");

        url.Should().BeNull();
    }

    [Fact]
    public async Task The_csv_is_fetched_only_once_across_repeated_lookups()
    {
        RecordingHandler handler = new(HttpStatusCode.OK, SampleCsv);
        YouTubeSuperStickerImageResolver resolver = Build(handler);

        await resolver.ResolveAsync("biggest_fans_heart_v2");
        await resolver.ResolveAsync("biggest_fans_blush_v2");
        await resolver.ResolveAsync("unknown");

        handler
            .RequestCount.Should()
            .Be(1, "the catalogue is loaded once and answered from memory after");
    }

    private static YouTubeSuperStickerImageResolver Build(RecordingHandler handler) =>
        new(
            new SingleClientFactory(handler),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<YouTubeSuperStickerImageResolver>.Instance
        );

    /// <summary>Answers every request with a fixed status + body, and counts how many it saw.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
