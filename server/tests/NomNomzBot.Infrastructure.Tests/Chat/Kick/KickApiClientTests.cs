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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Chat.Kick;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick transport's exact wire shapes against the verified public-v1 surface: send carries
/// content/type/broadcaster id (+ the native reply id) and parses the created message id; timeout POSTs
/// a minutes duration where ban omits it; unban is a bodied DELETE by (broadcaster, user); the 500-char
/// message and 100-char reason caps are enforced locally (no quota-burning guaranteed 400s); 401/403 map
/// to MISSING_SCOPE so an insufficient grant reads as a reauth need.
/// </summary>
public sealed class KickApiClientTests
{
    private const string Token = "kick-bearer-1";

    private static KickApiClient Build(StubHttpMessageHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("kick").Returns(new HttpClient(handler));
        return new KickApiClient(factory, NullLogger<KickApiClient>.Instance);
    }

    [Fact]
    public async Task Send_posts_the_message_into_the_channel_and_returns_the_message_id()
    {
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.OK, """{"data":{"message_id":"a1b2","is_sent":true}}""")
        );
        KickApiClient sut = Build(handler);

        Result<string> result = await sut.SendMessageAsync(Token, 12345, "hello kick");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("a1b2");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().EndWith("/public/v1/chat");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(Token);
        string body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("hello kick").And.Contain("12345").And.Contain("\"user\"");
    }

    [Fact]
    public async Task Send_threads_a_native_reply_when_a_reply_id_is_given()
    {
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.OK, """{"data":{"message_id":"a1b3","is_sent":true}}""")
        );
        KickApiClient sut = Build(handler);

        Result<string> result = await sut.SendMessageAsync(
            Token,
            12345,
            "threaded",
            replyToMessageId: "parent-9"
        );

        result.IsSuccess.Should().BeTrue();
        string body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain("parent-9");
    }

    [Fact]
    public async Task Send_rejects_an_over_500_char_message_before_any_call()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"));
        KickApiClient sut = Build(handler);

        Result<string> result = await sut.SendMessageAsync(Token, 12345, new string('k', 501));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Timeout_posts_a_minutes_duration_where_ban_omits_it()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"), (HttpStatusCode.OK, "{}"));
        KickApiClient sut = Build(handler);

        (await sut.TimeoutUserAsync(Token, 12345, 678, 10, "spam")).IsSuccess.Should().BeTrue();
        string timeoutBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        handler.LastRequest.RequestUri!.ToString().Should().EndWith("/public/v1/moderation/bans");
        timeoutBody.Should().Contain("\"duration\":10").And.Contain("678").And.Contain("spam");

        (await sut.BanUserAsync(Token, 12345, 678, "worse")).IsSuccess.Should().BeTrue();
        string banBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        banBody.Should().NotContain("duration").And.Contain("worse");
    }

    [Fact]
    public async Task A_moderation_reason_is_truncated_to_kicks_100_char_cap()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"));
        KickApiClient sut = Build(handler);
        string longReason = new('r', 150);

        await sut.BanUserAsync(Token, 12345, 678, longReason);

        string body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain(new string('r', 100)).And.NotContain(new string('r', 101));
    }

    [Fact]
    public async Task Unban_is_a_bodied_delete_by_broadcaster_and_user()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.NoContent, "{}"));
        KickApiClient sut = Build(handler);

        Result result = await sut.UnbanUserAsync(Token, 12345, 678);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().Should().EndWith("/public/v1/moderation/bans");
        string body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("12345").And.Contain("678");
    }

    [Fact]
    public async Task Delete_message_targets_the_message_id_route()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.NoContent, "{}"));
        KickApiClient sut = Build(handler);

        Result result = await sut.DeleteMessageAsync(Token, "m-9");

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().Should().EndWith("/public/v1/chat/m-9");
    }

    [Fact]
    public async Task A_403_maps_to_missing_scope()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.Forbidden, "{}"));
        KickApiClient sut = Build(handler);

        Result<string> result = await sut.SendMessageAsync(Token, 12345, "hi");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    /// <summary>Sequential scripted responses + the last request captured (content pre-buffered so body
    /// assertions survive disposal) — same shape as the YouTube client test stub.</summary>
    private sealed class StubHttpMessageHandler(
        params (HttpStatusCode Status, string Json)[] responses
    ) : HttpMessageHandler
    {
        private int _index;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync(cancellationToken);
            LastRequest = request;

            (HttpStatusCode status, string json) = responses[
                Math.Min(_index++, responses.Length - 1)
            ];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
