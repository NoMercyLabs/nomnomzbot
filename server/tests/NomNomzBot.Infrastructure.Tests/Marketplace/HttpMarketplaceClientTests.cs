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
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Infrastructure.Marketplace;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// Proves the marketplace wire client (marketplace.md §4): search maps the /v1 items page onto
/// <see cref="PagedList{T}"/>; download streams the bundle bytes; publish posts multipart WITH the vaulted
/// publisher bearer and maps the pending submission; submission status maps approved/rejected + note; an
/// unreachable or unconfigured marketplace is a typed <c>MARKETPLACE_UNAVAILABLE</c> failure, never a throw.
/// </summary>
public sealed class HttpMarketplaceClientTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c001");

    private static HttpMarketplaceClient Build(
        HttpMessageHandler handler,
        string? publisherToken = null,
        string url = "https://market.test"
    )
    {
        IMarketplacePublisherTokenService tokens =
            Substitute.For<IMarketplacePublisherTokenService>();
        tokens
            .GetPublisherTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(publisherToken);
        return new HttpMarketplaceClient(
            new SingleHandlerFactory(handler),
            tokens,
            Options.Create(new MarketplaceOptions { Url = url }),
            NullLogger<HttpMarketplaceClient>.Instance
        );
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_maps_the_wire_page_and_sends_the_filters()
    {
        ScriptedHandler handler = new(_ =>
            Json(
                """
                {
                  "items": [
                    {
                      "itemId": "itm_1",
                      "name": "Starter Pack",
                      "author": "stoney",
                      "version": "1.2.0",
                      "summary": "greets people",
                      "type": "command",
                      "tags": ["fun", "chat"],
                      "capabilities": ["sends chat messages"],
                      "rating": 4.5,
                      "installs": 321
                    }
                  ],
                  "page": 2,
                  "pageSize": 10,
                  "totalCount": 25
                }
                """
            )
        );
        HttpMarketplaceClient client = Build(handler);

        Result<PagedList<MarketplaceItemDto>> result = await client.SearchAsync(
            new MarketplaceQuery("greet", "command", ["fun"]),
            new PaginationParams(2, 10)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(10);
        result.Value.TotalCount.Should().Be(25);
        MarketplaceItemDto item = result.Value.Items.Should().ContainSingle().Subject;
        item.ItemId.Should().Be("itm_1");
        item.Name.Should().Be("Starter Pack");
        item.Author.Should().Be("stoney");
        item.Version.Should().Be("1.2.0");
        item.Summary.Should().Be("greets people");
        item.Type.Should().Be("command");
        item.Tags.Should().Equal("fun", "chat");
        item.Capabilities.Should().Equal("sends chat messages");
        item.Rating.Should().Be(4.5);
        item.Installs.Should().Be(321);

        string uri = handler.Requests.Should().ContainSingle().Subject.RequestUri!.ToString();
        uri.Should().StartWith("https://market.test/v1/items?");
        uri.Should().Contain("q=greet").And.Contain("type=command").And.Contain("tags=fun");
        uri.Should().Contain("page=2").And.Contain("pageSize=10");
    }

    // ── Download ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_streams_the_bundle_bytes()
    {
        byte[] payload = Encoding.UTF8.GetBytes("PK-this-is-the-bundle");
        ScriptedHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        });
        HttpMarketplaceClient client = Build(handler);

        Result<System.IO.Stream> result = await client.DownloadAsync("itm_1");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using MemoryStream received = new();
        await result.Value.CopyToAsync(received);
        received.ToArray().Should().Equal(payload);
        handler
            .Requests.Single()
            .RequestUri!.ToString()
            .Should()
            .Be("https://market.test/v1/items/itm_1/download");
    }

    // ── Publish ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_posts_multipart_with_the_publisher_bearer_and_maps_pending()
    {
        ScriptedHandler handler = new(_ =>
            Json("""{ "submissionId": "sub_9", "status": "pending", "reviewNote": null }""")
        );
        HttpMarketplaceClient client = Build(handler, publisherToken: "pub-token-123");

        using MemoryStream zip = new(Encoding.UTF8.GetBytes("PK-zip-bytes"));
        Result<PublishSubmissionDto> result = await client.PublishAsync(
            Channel,
            zip,
            new PublishMetadata("Starter Pack", "1.2.0", "greets people", ["fun"])
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.SubmissionId.Should().Be("sub_9");
        result.Value.Status.Should().Be("pending");
        result.Value.ReviewNote.Should().BeNull();

        HttpRequestMessage request = handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.ToString().Should().Be("https://market.test/v1/publish");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("pub-token-123");
        string body = handler.RequestBodies.Single();
        body.Should().Contain("PK-zip-bytes"); // the bundle part
        body.Should().Contain("Starter Pack").And.Contain("1.2.0"); // the metadata part
    }

    [Fact]
    public async Task Publish_without_a_stored_token_is_refused_before_any_request()
    {
        ScriptedHandler handler = new(_ =>
            Json("""{ "submissionId": "sub_9", "status": "pending", "reviewNote": null }""")
        );
        HttpMarketplaceClient client = Build(handler, publisherToken: null);

        using MemoryStream zip = new([1, 2, 3]);
        Result<PublishSubmissionDto> result = await client.PublishAsync(
            Channel,
            zip,
            new PublishMetadata("x", "1.0.0", null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MARKETPLACE_NO_PUBLISHER_TOKEN");
        handler.Requests.Should().BeEmpty();
    }

    // ── Submission status ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("approved", null)]
    [InlineData("rejected", "contains undeclared run_code")]
    public async Task Submission_maps_the_vetting_status_and_note(string status, string? note)
    {
        string noteJson = note is null ? "null" : $"\"{note}\"";
        ScriptedHandler handler = new(_ =>
            Json(
                $$"""{ "submissionId": "sub_9", "status": "{{status}}", "reviewNote": {{noteJson}} }"""
            )
        );
        HttpMarketplaceClient client = Build(handler, publisherToken: "pub-token-123");

        Result<PublishSubmissionDto> result = await client.GetSubmissionAsync(Channel, "sub_9");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Status.Should().Be(status);
        result.Value.ReviewNote.Should().Be(note);
        HttpRequestMessage request = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://market.test/v1/submissions/sub_9");
        request.Headers.Authorization!.Parameter.Should().Be("pub-token-123");
    }

    // ── Failure posture ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Unreachable_marketplace_is_a_typed_failure_not_a_throw()
    {
        ScriptedHandler handler = new(_ => throw new HttpRequestException("connection refused"));
        HttpMarketplaceClient client = Build(handler);

        Result<PagedList<MarketplaceItemDto>> search = await client.SearchAsync(
            new MarketplaceQuery()
        );
        Result<System.IO.Stream> download = await client.DownloadAsync("itm_1");

        search.IsFailure.Should().BeTrue();
        search.ErrorCode.Should().Be("MARKETPLACE_UNAVAILABLE");
        download.IsFailure.Should().BeTrue();
        download.ErrorCode.Should().Be("MARKETPLACE_UNAVAILABLE");
    }

    [Fact]
    public async Task Unconfigured_marketplace_url_is_a_typed_failure_without_any_request()
    {
        ScriptedHandler handler = new(_ => Json("{}"));
        HttpMarketplaceClient client = Build(handler, url: "");

        Result<MarketplaceItemDto> result = await client.GetItemAsync("itm_1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MARKETPLACE_UNAVAILABLE");
        handler.Requests.Should().BeEmpty();
    }

    // ── Test doubles ────────────────────────────────────────────────────────────

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>Scripts every response, recording each request and its body for assertions.</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            return respond(request);
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
