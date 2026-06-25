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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Sandbox;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the per-execution host bridge (custom-code.md §3.1/§6.2): chat.send dispatches to the channel's Helix
/// chat provider with the tenant Guid (the guest only holds the Guid; the provider resolves the Twitch id);
/// economy.read reads the channel ledger for the trigger user; a granted-but-unwired capability resolves to a no-op.
/// </summary>
public sealed class ScriptHostBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-00000000e0a2");

    private static ScriptHostBridge Build(
        IChatProvider? chat = null,
        ICurrencyAccountService? currency = null,
        IMusicService? music = null,
        IHttpClientFactory? http = null
    ) =>
        new(
            Channel,
            Viewer.ToString(),
            chat ?? Substitute.For<IChatProvider>(),
            currency ?? Substitute.For<ICurrencyAccountService>(),
            music ?? Substitute.For<IMusicService>(),
            http ?? Substitute.For<IHttpClientFactory>()
        );

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }

    [Fact]
    public async Task Chat_send_dispatches_to_the_chat_provider_with_the_tenant_guid()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        ScriptHostBridge bridge = Build(chat);

        string? result = bridge.Resolve("chat.send")(
            "chat.send",
            ["hello world"],
            CancellationToken.None
        );

        result.Should().BeNull();
        await chat.Received()
            .SendMessageAsync(Channel, "hello world", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Economy_read_returns_the_trigger_users_balance()
    {
        ICurrencyAccountService currency = Substitute.For<ICurrencyAccountService>();
        currency
            .GetBalanceAsync(Channel, Viewer, Arg.Any<CancellationToken>())
            .Returns(Result.Success(500L));
        ScriptHostBridge bridge = Build(currency: currency);

        bridge
            .Resolve("economy.read")("economy.read", [], CancellationToken.None)
            .Should()
            .Be("500");
    }

    [Fact]
    public void Music_queue_enqueues_the_request_for_the_channel()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .AddToQueueAsync(
                Channel.ToString(),
                "lofi beats",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        ScriptHostBridge bridge = Build(music: music);

        bridge
            .Resolve("music.queue")("music.queue", ["lofi beats"], CancellationToken.None)
            .Should()
            .Be("true");
    }

    [Fact]
    public void Http_fetch_returns_the_capped_response_body()
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(EgressHttpClient.Name)
            .Returns(new HttpClient(new StubHandler("hello from the web")));
        ScriptHostBridge bridge = Build(http: factory);

        bridge
            .Resolve("http.fetch")(
                "http.fetch",
                ["https://example.com/data"],
                CancellationToken.None
            )
            .Should()
            .Be("hello from the web");
    }

    [Fact]
    public void Http_fetch_rejects_a_non_https_url()
    {
        Build()
            .Resolve("http.fetch")("http.fetch", ["http://example.com"], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public void A_granted_but_unwired_capability_is_a_noop()
    {
        Build()
            .Resolve("moderation.timeout")("moderation.timeout", ["user"], CancellationToken.None)
            .Should()
            .BeNull();
    }
}
