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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Auth;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the app-access-token provider mints the <c>client_credentials</c> token, hands it back, and — because
/// the token is long-lived — caches it so a second request makes NO second HTTP call; that it re-mints only after
/// an explicit invalidation (the 401 path); and that a credential-less deployment fails cleanly (so the chat send
/// falls back to the user token rather than sending a malformed request). This is the badge-bearing token behind
/// the chatbot badge, so its lifecycle is asserted by behavior (HTTP calls made, grant type sent), not surface.
/// </summary>
public sealed class TwitchAppTokenProviderTests
{
    [Fact]
    public async Task GetAppToken_MintsViaClientCredentials_ReturnsTheIssuedToken()
    {
        RecordingAppTokenHandler handler = new();
        TwitchAppTokenProvider provider = Build(
            handler,
            new SystemAppCredentials("cid-42", "sec-99")
        );

        Result<string> result = await provider.GetAppTokenAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("app-token-abc", "the provider returns the token Twitch issued");
        handler.CallCount.Should().Be(1);
        handler.LastBody.Should().NotBeNull();
        handler
            .LastBody!.Should()
            .Contain("grant_type=client_credentials", "the badge token is an app token");
        handler.LastBody!.Should().Contain("client_id=cid-42");
        handler.LastBody!.Should().Contain("client_secret=sec-99");
    }

    [Fact]
    public async Task GetAppToken_CachesTheToken_SecondCallMakesNoSecondHttpRequest()
    {
        RecordingAppTokenHandler handler = new();
        TwitchAppTokenProvider provider = Build(handler, new SystemAppCredentials("cid", "sec"));

        Result<string> first = await provider.GetAppTokenAsync();
        Result<string> second = await provider.GetAppTokenAsync();

        first.Value.Should().Be("app-token-abc");
        second.Value.Should().Be("app-token-abc", "the cached token is returned unchanged");
        handler.CallCount.Should().Be(1, "a still-valid token is served from cache, not re-minted");
    }

    [Fact]
    public async Task Invalidate_ForcesAReMint_OnTheNextCall()
    {
        RecordingAppTokenHandler handler = new();
        TwitchAppTokenProvider provider = Build(handler, new SystemAppCredentials("cid", "sec"));

        await provider.GetAppTokenAsync();
        provider.Invalidate();
        await provider.GetAppTokenAsync();

        handler.CallCount.Should().Be(2, "invalidation drops the cache so the next call re-mints");
    }

    [Fact]
    public async Task GetAppToken_WithoutConfiguredCredentials_FailsCleanlyWithoutCallingTwitch()
    {
        RecordingAppTokenHandler handler = new();
        TwitchAppTokenProvider provider = Build(handler, credentials: null);

        Result<string> result = await provider.GetAppTokenAsync();

        result.IsFailure.Should().BeTrue("a secret-less deployment cannot mint an app token");
        handler.CallCount.Should().Be(0, "no HTTP request is made when credentials are absent");
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static TwitchAppTokenProvider Build(
        HttpMessageHandler handler,
        SystemAppCredentials? credentials
    )
    {
        ISystemCredentialsProvider creds = Substitute.For<ISystemCredentialsProvider>();
        creds
            .GetAsync("twitch", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(credentials));

        return new TwitchAppTokenProvider(
            ScopeFactoryFor(creds),
            new SingleClientFactory(handler),
            TimeProvider.System,
            NullLogger<TwitchAppTokenProvider>.Instance
        );
    }

    /// <summary>An <see cref="IServiceScopeFactory"/> whose scope resolves the given credentials provider —
    /// mirrors how the singleton opens a scope per mint to read the scoped credentials.</summary>
    private static IServiceScopeFactory ScopeFactoryFor(ISystemCredentialsProvider credentials)
    {
        IServiceProvider sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ISystemCredentialsProvider)).Returns(credentials);
        IServiceScope scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        IServiceScopeFactory factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    /// <summary>Records the token-endpoint request body and returns a canned app-token response.</summary>
    private sealed class RecordingAppTokenHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"app-token-abc","expires_in":5000000,"token_type":"bearer"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
