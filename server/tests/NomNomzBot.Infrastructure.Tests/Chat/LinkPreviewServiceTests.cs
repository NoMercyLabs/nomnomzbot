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
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Sandbox;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the link-preview service (chat-decoration spec §3.5): it scrapes the og:title/description/image tags into a
/// preview (tolerating either meta-attribute order), returns no preview for non-html content, and serves a repeated
/// url from cache instead of re-fetching.
/// </summary>
public sealed class LinkPreviewServiceTests
{
    private const string Html = """
        <html><head>
          <meta property="og:title" content="Cool Page">
          <meta name="og:description" content="A nice &amp; tidy description">
          <meta content="https://img.example/x.png" property="og:image">
        </head></html>
        """;

    private static LinkPreviewService Service(StubHandler handler, ICacheService cache)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(EgressHttpClient.Name).Returns(_ => new HttpClient(handler));
        return new LinkPreviewService(factory, cache, NullLogger<LinkPreviewService>.Instance);
    }

    [Fact]
    public async Task Parses_open_graph_tags_into_a_preview()
    {
        Result<LinkPreview?> result = await Service(
                new StubHandler(Html, "text/html"),
                new FakeCache()
            )
            .FetchAsync(new Uri("https://example.com/page"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        LinkPreview preview = result.Value!;
        preview.Host.Should().Be("example.com");
        preview.Title.Should().Be("Cool Page");
        preview.Description.Should().Be("A nice & tidy description"); // entity decoded, attribute order tolerated
        preview.ImageUrl.Should().Be("https://img.example/x.png");
    }

    [Fact]
    public async Task Non_html_content_yields_no_preview()
    {
        Result<LinkPreview?> result = await Service(
                new StubHandler("binary-image-bytes", "image/png"),
                new FakeCache()
            )
            .FetchAsync(new Uri("https://example.com/x.png"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task A_second_request_for_the_same_url_is_served_from_cache()
    {
        StubHandler handler = new(Html, "text/html");
        LinkPreviewService service = Service(handler, new FakeCache());

        await service.FetchAsync(new Uri("https://example.com/page"));
        await service.FetchAsync(new Uri("https://example.com/page"));

        handler.Calls.Should().Be(1); // the second request hit the cache, no re-fetch
    }

    private sealed class StubHandler(string body, string contentType) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, contentType),
                }
            );
        }
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }
}
