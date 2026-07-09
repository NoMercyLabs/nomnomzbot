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
/// Behavioural proof for <see cref="GoogleYouTubeLoginProvider"/> (platform-identity §3.2): the device grant is
/// started and parsed from Google's canned JSON, a pending poll surfaces the <see cref="DeviceLoginStatus"/>
/// continuation code, and an approved poll proves the OpenID identity and vaults the issued tokens before
/// returning the <see cref="ExternalIdentityProof"/>.
/// </summary>
public sealed class GoogleYouTubeLoginProviderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ConnectionId = Guid.Parse("0192a000-0000-7000-8000-00000000ab01");

    private const string DeviceCodeJson =
        """{"device_code":"DEV-GOOG-1","user_code":"WXYZ-1234","verification_url":"https://www.google.com/device","expires_in":1800,"interval":5}""";

    private const string TokenJson =
        """{"access_token":"goog-access","refresh_token":"goog-refresh","expires_in":3600,"scope":"openid email profile","token_type":"Bearer"}""";

    private const string UserInfoJson =
        """{"sub":"goog-sub-42","name":"The Creator","picture":"https://cdn.google/p.png"}""";

    [Fact]
    public async Task StartDeviceAsync_parses_the_google_device_authorization_response()
    {
        StubHandler handler = new();
        handler.When(DeviceCodeEndpoint, HttpStatusCode.OK, DeviceCodeJson);
        GoogleYouTubeLoginProvider provider = Build(handler, out _);

        Result<DeviceCodeStartDto> result = await provider.StartDeviceAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.DeviceCode.Should().Be("DEV-GOOG-1");
        result.Value.UserCode.Should().Be("WXYZ-1234");
        result.Value.VerificationUri.Should().Be("https://www.google.com/device");
        result.Value.Interval.Should().Be(5);
        result.Value.ExpiresIn.Should().Be(1800);

        // The device request carries the resolved client id + the OpenID scope set — Google's device endpoint.
        handler.LastUri(DeviceCodeEndpoint).Should().Be(new Uri(DeviceCodeEndpoint));
        string body = WebUtility.UrlDecode(handler.LastBody(DeviceCodeEndpoint)!);
        body.Should().Contain("client_id=yt-client-id");
        body.Should().Contain("scope=openid email profile");
    }

    [Fact]
    public async Task PollDeviceAsync_when_authorization_pending_fails_with_the_pending_status_code()
    {
        StubHandler handler = new();
        handler.When(
            TokenEndpoint,
            HttpStatusCode.BadRequest,
            """{"error":"authorization_pending"}"""
        );
        GoogleYouTubeLoginProvider provider = Build(handler, out _);

        Result<ExternalIdentityProof> result = await provider.PollDeviceAsync("DEV-GOOG-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(DeviceLoginStatus.Pending);
    }

    [Fact]
    public async Task PollDeviceAsync_when_approved_proves_the_identity_and_vaults_the_tokens()
    {
        StubHandler handler = new();
        handler.When(TokenEndpoint, HttpStatusCode.OK, TokenJson);
        handler.When(UserInfoEndpoint, HttpStatusCode.OK, UserInfoJson);
        GoogleYouTubeLoginProvider provider = Build(handler, out IIntegrationTokenVault vault);

        Result<ExternalIdentityProof> result = await provider.PollDeviceAsync("DEV-GOOG-1");

        result.IsSuccess.Should().BeTrue();
        ExternalIdentityProof proof = result.Value;
        proof.Provider.Should().Be(AuthEnums.LoginProvider.YouTube);
        proof.ProviderUserId.Should().Be("goog-sub-42");
        proof.Username.Should().Be("The Creator");
        proof.DisplayName.Should().Be("The Creator");
        proof.AvatarUrl.Should().Be("https://cdn.google/p.png");
        proof.ConnectionId.Should().Be(ConnectionId);

        // The issued access token is sealed into the vaulted connection with the OpenID scope set + expiry.
        await vault
            .Received(1)
            .StoreTokensAsync(
                ConnectionId,
                Arg.Is<StoreTokensDto>(t =>
                    t.AccessToken == "goog-access"
                    && t.RefreshToken == "goog-refresh"
                    && t.AccessExpiresAt == FixedNow.UtcDateTime.AddSeconds(3600)
                ),
                Arg.Is<IReadOnlyList<string>>(s =>
                    s.Contains("openid") && s.Contains("email") && s.Contains("profile")
                ),
                Arg.Any<CancellationToken>()
            );

        // The identity call carried the freshly issued bearer token.
        handler.LastAuthorization(UserInfoEndpoint).Should().Be("Bearer goog-access");
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    private static GoogleYouTubeLoginProvider Build(
        StubHandler handler,
        out IIntegrationTokenVault vault
    )
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync(AuthEnums.LoginProvider.YouTube, Arg.Any<CancellationToken>())
            .Returns("yt-client-id");
        credentials
            .GetAsync(AuthEnums.LoginProvider.YouTube, Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("yt-client-id", "yt-client-secret"));

        vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new IntegrationConnectionDto(
                        Id: ConnectionId,
                        BroadcasterId: null,
                        Provider: AuthEnums.LoginProvider.YouTube,
                        ProviderAccountId: "goog-sub-42",
                        ProviderAccountName: "The Creator",
                        Status: AuthEnums.IntegrationStatus.Connected,
                        Scopes: ["openid", "email", "profile"],
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

        return new GoogleYouTubeLoginProvider(
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
        private readonly Dictionary<string, Uri> _lastUri = new();
        private readonly Dictionary<string, string?> _lastBody = new();
        private readonly Dictionary<string, string?> _lastAuthorization = new();

        public void When(string endpoint, HttpStatusCode status, string body) =>
            _responses[endpoint] = (status, body);

        public Uri? LastUri(string endpoint) =>
            _lastUri.TryGetValue(endpoint, out Uri? uri) ? uri : null;

        public string? LastBody(string endpoint) =>
            _lastBody.TryGetValue(endpoint, out string? body) ? body : null;

        public string? LastAuthorization(string endpoint) =>
            _lastAuthorization.TryGetValue(endpoint, out string? auth) ? auth : null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string endpoint = request.RequestUri!.GetLeftPart(UriPartial.Path);
            _lastUri[endpoint] = request.RequestUri;
            if (request.Content is not null)
                _lastBody[endpoint] = await request.Content.ReadAsStringAsync(cancellationToken);
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
