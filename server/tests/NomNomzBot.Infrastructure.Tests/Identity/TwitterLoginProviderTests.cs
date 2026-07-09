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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.Login;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="TwitterLoginProvider"/> (platform-identity §10.3): the authorize URL carries
/// the resolved client id, the caller's state, and the PKCE S256 challenge; a code exchange authenticates the
/// token request with HTTP Basic auth (X's confidential-client rule), parses <c>/2/users/me</c>, proves the
/// identity, and vaults the issued tokens before returning the <see cref="ExternalIdentityProof"/>.
/// </summary>
public sealed class TwitterLoginProviderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ConnectionId = Guid.Parse("0192a000-0000-7000-8000-00000000dd01");

    private const string RedirectUri = "https://api.nomnomz.bot/api/v1/auth/twitter/callback";

    private const string TokenJson =
        """{"access_token":"tw-access","refresh_token":"tw-refresh","expires_in":7200,"token_type":"bearer","scope":"users.read tweet.read offline.access"}""";

    private const string UserMeJson =
        """{"data":{"id":"1465","name":"The Poster","username":"theposter"}}""";

    [Fact]
    public async Task BuildAuthorizeUrlAsync_carries_the_client_id_state_and_pkce_challenge()
    {
        StubHandler handler = new();
        TwitterLoginProvider provider = Build(handler, out _);

        Result<Uri> result = await provider.BuildAuthorizeUrlAsync(
            state: "state-xyz",
            redirectUri: RedirectUri,
            codeChallenge: "challenge-abc"
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.GetLeftPart(UriPartial.Path)
            .Should()
            .Be("https://twitter.com/i/oauth2/authorize");
        string query = WebUtility.UrlDecode(result.Value.Query);
        query.Should().Contain("client_id=tw-client-id");
        query.Should().Contain("state=state-xyz");
        query.Should().Contain("code_challenge=challenge-abc");
        query.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task ExchangeCodeAsync_basic_auths_the_token_request_proves_identity_and_vaults_tokens()
    {
        StubHandler handler = new();
        handler.When(TokenEndpoint, HttpStatusCode.OK, TokenJson);
        handler.When(UserInfoEndpoint, HttpStatusCode.OK, UserMeJson);
        TwitterLoginProvider provider = Build(handler, out IIntegrationTokenVault vault);

        Result<ExternalIdentityProof> result = await provider.ExchangeCodeAsync(
            code: "auth-code-1",
            redirectUri: RedirectUri,
            codeVerifier: "verifier-1"
        );

        result.IsSuccess.Should().BeTrue();
        ExternalIdentityProof proof = result.Value;
        proof.Provider.Should().Be(AuthEnums.LoginProvider.Twitter);
        proof.ProviderUserId.Should().Be("1465");
        proof.Username.Should().Be("theposter");
        proof.DisplayName.Should().Be("The Poster");
        proof.ConnectionId.Should().Be(ConnectionId);

        // The confidential client authenticated the token request with HTTP Basic auth over client id:secret.
        string expectedBasic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("tw-client-id:tw-client-secret")
        );
        handler.LastAuthorization(TokenEndpoint).Should().Be($"Basic {expectedBasic}");

        // The issued access token is sealed into the vaulted connection with the login scope set + expiry.
        await vault
            .Received(1)
            .StoreTokensAsync(
                ConnectionId,
                Arg.Is<StoreTokensDto>(t =>
                    t.AccessToken == "tw-access"
                    && t.RefreshToken == "tw-refresh"
                    && t.AccessExpiresAt == FixedNow.UtcDateTime.AddSeconds(7200)
                ),
                Arg.Is<IReadOnlyList<string>>(s =>
                    s.Contains("users.read")
                    && s.Contains("tweet.read")
                    && s.Contains("offline.access")
                ),
                Arg.Any<CancellationToken>()
            );

        // The identity call carried the freshly issued bearer token.
        handler.LastAuthorization(UserInfoEndpoint).Should().Be("Bearer tw-access");
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private const string TokenEndpoint = "https://api.x.com/2/oauth2/token";
    private const string UserInfoEndpoint = "https://api.x.com/2/users/me";

    private static TwitterLoginProvider Build(StubHandler handler, out IIntegrationTokenVault vault)
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync(AuthEnums.LoginProvider.Twitter, Arg.Any<CancellationToken>())
            .Returns("tw-client-id");
        credentials
            .GetAsync(AuthEnums.LoginProvider.Twitter, Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("tw-client-id", "tw-client-secret"));

        vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new IntegrationConnectionDto(
                        Id: ConnectionId,
                        BroadcasterId: null,
                        Provider: AuthEnums.LoginProvider.Twitter,
                        ProviderAccountId: "1465",
                        ProviderAccountName: "The Poster",
                        Status: AuthEnums.IntegrationStatus.Connected,
                        Scopes: ["users.read", "tweet.read", "offline.access"],
                        IsByok: false,
                        ConnectedAt: FixedNow.UtcDateTime,
                        LastRefreshedAt: null,
                        ConsecutiveFailureCount: 0
                    )
                )
            );
        vault
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Any<StoreTokensDto>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        return new TwitterLoginProvider(
            new SingleClientFactory(handler),
            credentials,
            vault,
            new FakeTimeProvider(FixedNow)
        );
    }

    /// <summary>Routes each request to the canned response registered for its endpoint, recording what was sent.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses =
            new();
        private readonly Dictionary<string, string?> _lastAuthorization = new();

        public void When(string endpoint, HttpStatusCode status, string body) =>
            _responses[endpoint] = (status, body);

        public string? LastAuthorization(string endpoint) =>
            _lastAuthorization.TryGetValue(endpoint, out string? auth) ? auth : null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string endpoint = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (request.Headers.Authorization is not null)
                _lastAuthorization[endpoint] = request.Headers.Authorization.ToString();
            else if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
                _lastAuthorization[endpoint] = string.Join(' ', values);

            (HttpStatusCode status, string body) = _responses[endpoint];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
