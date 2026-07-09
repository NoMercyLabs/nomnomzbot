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
/// Behavioural proof for <see cref="KickLoginProvider"/> (platform-identity §10.3): the authorize URL carries the
/// resolved client id, the caller's state, and the PKCE S256 challenge; a code exchange parses Kick's token +
/// <c>/public/v1/users</c> envelope, proves the identity, and vaults the issued tokens before returning the
/// <see cref="ExternalIdentityProof"/>.
/// </summary>
public sealed class KickLoginProviderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ConnectionId = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");

    private const string RedirectUri = "https://api.nomnomz.bot/api/v1/auth/kick/callback";

    private const string TokenJson =
        """{"access_token":"kick-access","refresh_token":"kick-refresh","expires_in":3600,"token_type":"Bearer","scope":"user:read"}""";

    private const string UsersJson =
        """{"data":[{"user_id":778,"name":"kickstreamer","email":"streamer@kick.com","profile_picture":"https://cdn.kick/p.png"}],"message":"OK"}""";

    [Fact]
    public async Task BuildAuthorizeUrlAsync_carries_the_client_id_state_and_pkce_challenge()
    {
        StubHandler handler = new();
        KickLoginProvider provider = Build(handler, out _);

        Result<Uri> result = await provider.BuildAuthorizeUrlAsync(
            state: "state-xyz",
            redirectUri: RedirectUri,
            codeChallenge: "challenge-abc"
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.GetLeftPart(UriPartial.Path)
            .Should()
            .Be("https://id.kick.com/oauth/authorize");
        string query = WebUtility.UrlDecode(result.Value.Query);
        query.Should().Contain("client_id=kick-client-id");
        query.Should().Contain("state=state-xyz");
        query.Should().Contain("code_challenge=challenge-abc");
        query.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task ExchangeCodeAsync_when_approved_proves_the_identity_and_vaults_the_tokens()
    {
        StubHandler handler = new();
        handler.When(TokenEndpoint, HttpStatusCode.OK, TokenJson);
        handler.When(UserInfoEndpoint, HttpStatusCode.OK, UsersJson);
        KickLoginProvider provider = Build(handler, out IIntegrationTokenVault vault);

        Result<ExternalIdentityProof> result = await provider.ExchangeCodeAsync(
            code: "auth-code-1",
            redirectUri: RedirectUri,
            codeVerifier: "verifier-1"
        );

        result.IsSuccess.Should().BeTrue();
        ExternalIdentityProof proof = result.Value;
        proof.Provider.Should().Be(AuthEnums.LoginProvider.Kick);
        proof.ProviderUserId.Should().Be("778");
        proof.Username.Should().Be("kickstreamer");
        proof.DisplayName.Should().Be("kickstreamer");
        proof.AvatarUrl.Should().Be("https://cdn.kick/p.png");
        proof.ConnectionId.Should().Be(ConnectionId);

        // The issued access token is sealed into the vaulted connection with the login scope set + expiry.
        await vault
            .Received(1)
            .StoreTokensAsync(
                ConnectionId,
                Arg.Is<StoreTokensDto>(t =>
                    t.AccessToken == "kick-access"
                    && t.RefreshToken == "kick-refresh"
                    && t.AccessExpiresAt == FixedNow.UtcDateTime.AddSeconds(3600)
                ),
                Arg.Is<IReadOnlyList<string>>(s => s.Contains("user:read")),
                Arg.Any<CancellationToken>()
            );

        // The identity call carried the freshly issued bearer token.
        handler.LastAuthorization(UserInfoEndpoint).Should().Be("Bearer kick-access");
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private const string TokenEndpoint = "https://id.kick.com/oauth/token";
    private const string UserInfoEndpoint = "https://api.kick.com/public/v1/users";

    private static KickLoginProvider Build(StubHandler handler, out IIntegrationTokenVault vault)
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync(AuthEnums.LoginProvider.Kick, Arg.Any<CancellationToken>())
            .Returns("kick-client-id");
        credentials
            .GetAsync(AuthEnums.LoginProvider.Kick, Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("kick-client-id", "kick-client-secret"));

        vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new IntegrationConnectionDto(
                        Id: ConnectionId,
                        BroadcasterId: null,
                        Provider: AuthEnums.LoginProvider.Kick,
                        ProviderAccountId: "778",
                        ProviderAccountName: "kickstreamer",
                        Status: AuthEnums.IntegrationStatus.Connected,
                        Scopes: ["user:read"],
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

        return new KickLoginProvider(
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
            if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
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
