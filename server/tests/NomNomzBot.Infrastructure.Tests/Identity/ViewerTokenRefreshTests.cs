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

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NomNomzBot.Infrastructure.Tests.Gdpr;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A viewer's or moderator's own Twitch token (no channel) refreshes by its own connection id, over the real
/// vault, resolver and auth service. A refresh posts only that user's refresh token to Twitch and rewrites only
/// that user's row. Another user's token is never read, posted or rotated.
/// </summary>
public sealed class ViewerTokenRefreshTests
{
    [Fact]
    public async Task An_expired_viewer_token_is_refreshed_before_use_and_only_that_viewers_row_changes()
    {
        Harness harness = await Harness.BuildAsync();
        User alice = await harness.AddViewerAsync("tw-alice", "alice", DateTime.UtcNow.AddHours(3));
        User bob = await harness.AddViewerAsync("tw-bob", "bob", DateTime.UtcNow.AddMinutes(-5));

        Result<TwitchAccessContext> bobToken = await harness.Resolver.GetUserTokenAsync(bob.Id);
        Result<TwitchAccessContext> aliceToken = await harness.Resolver.GetUserTokenAsync(alice.Id);

        bobToken.Value.AccessToken.Should().Be("fresh-for-bob-refresh");
        aliceToken.Value.AccessToken.Should().Be("alice-access");
        harness.Wire.PostedRefreshTokens.Should().Equal("bob-refresh");
        (await harness.RefreshTokenOfAsync(bob)).Should().Be("next-bob-refresh");
        (await harness.RefreshTokenOfAsync(alice)).Should().Be("alice-refresh");
    }

    [Fact]
    public async Task A_send_that_gets_a_401_refreshes_the_senders_own_token_and_no_one_elses()
    {
        Harness harness = await Harness.BuildAsync();
        User alice = await harness.AddViewerAsync("tw-alice", "alice", DateTime.UtcNow.AddHours(3));
        User bob = await harness.AddViewerAsync("tw-bob", "bob", DateTime.UtcNow.AddHours(3));
        Result<TwitchAccessContext> bobToken = await harness.Resolver.GetUserTokenAsync(bob.Id);

        Result<TwitchAccessContext> retried = await harness.Resolver.RefreshAsync(bobToken.Value);

        retried.IsSuccess.Should().BeTrue();
        retried.Value.AccessToken.Should().Be("fresh-for-bob-refresh");
        harness.Wire.PostedRefreshTokens.Should().Equal("bob-refresh");
        (await harness.RefreshTokenOfAsync(alice)).Should().Be("alice-refresh");
        (await harness.RefreshTokenOfAsync(bob)).Should().Be("next-bob-refresh");
    }

    [Fact]
    public async Task The_channel_less_twitch_pair_still_refuses_to_pick_a_row()
    {
        Harness harness = await Harness.BuildAsync();
        await harness.AddViewerAsync("tw-alice", "alice", DateTime.UtcNow.AddHours(3));

        TokenResult? result = await harness.Auth.RefreshTokenAsync(
            null,
            AuthEnums.IntegrationProvider.Twitch
        );

        result.Should().BeNull();
        harness.Wire.PostedRefreshTokens.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public required AuthDbContext Db { get; init; }
        public required IntegrationTokenVault Vault { get; init; }
        public required TwitchAuthService Auth { get; init; }
        public required TwitchTokenResolver Resolver { get; init; }
        public required RecordingWire Wire { get; init; }

        public static async Task<Harness> BuildAsync()
        {
            AuthDbContext db = AuthTestBuilder.NewContext();
            ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
                db,
                out ISubjectKeyService keys
            );
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "twitch.client_id",
                    Value = "app-id",
                }
            );
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "twitch.client_secret",
                    SecureValue = await protector.ProtectAsync(
                        "app-secret",
                        SystemCredentialsProvider.ContextFor("twitch.client_secret")
                    ),
                }
            );
            await db.SaveChangesAsync();

            IntegrationTokenVault vault = new(
                db,
                protector,
                keys,
                new NoopScopeGrantService(),
                new RecordingEventBus(),
                TimeProvider.System,
                NullLogger<IntegrationTokenVault>.Instance
            );
            RecordingWire wire = new();
            TwitchAuthService auth = new(
                db,
                vault,
                AuthTestBuilder.CredentialsProvider(
                    db,
                    protector,
                    new ConfigurationBuilder().Build()
                ),
                new WireClientFactory(wire),
                NullLogger<TwitchAuthService>.Instance,
                TimeProvider.System,
                new ConnectionRefreshGate()
            );
            TwitchTokenResolver resolver = new(
                db,
                vault,
                auth,
                Substitute.For<ITwitchAppTokenProvider>(),
                new RecordingEventBus(),
                Substitute.For<IDeploymentProfileService>()
            );

            return new()
            {
                Db = db,
                Vault = vault,
                Auth = auth,
                Resolver = resolver,
                Wire = wire,
            };
        }

        public async Task<User> AddViewerAsync(string twitchUserId, string name, DateTime expiresAt)
        {
            User user = new()
            {
                TwitchUserId = twitchUserId,
                Platform = AuthEnums.Platform.Twitch,
                Username = name,
                UsernameNormalized = name,
                DisplayName = name,
                Enabled = true,
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();

            Result<IntegrationConnectionDto> connection = await Vault.UpsertConnectionAsync(
                new(
                    null,
                    AuthEnums.IntegrationProvider.Twitch,
                    twitchUserId,
                    name,
                    ["user:write:chat"],
                    "cid",
                    IsByok: false,
                    user.Id,
                    SettingsJson: null
                )
            );
            await Vault.StoreTokensAsync(
                connection.Value.Id,
                new($"{name}-access", $"{name}-refresh", AppToken: null, expiresAt),
                ["user:write:chat"]
            );
            return user;
        }

        public async Task<string> RefreshTokenOfAsync(User user)
        {
            Guid connectionId = await Db
                .IntegrationConnections.IgnoreQueryFilters()
                .Where(c => c.ProviderAccountId == user.TwitchUserId)
                .Select(c => c.Id)
                .SingleAsync();
            return (await Vault.GetRefreshTokenAsync(connectionId)).Value.Value;
        }
    }

    /// <summary>Records each refresh token posted to Twitch and answers with a token derived from it.</summary>
    private sealed class RecordingWire : HttpMessageHandler
    {
        public List<string> PostedRefreshTokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            string posted = body.Split('&')
                .Select(pair => pair.Split('='))
                .First(parts => parts[0] == "refresh_token")[1];
            PostedRefreshTokens.Add(posted);

            string json =
                $$"""{"access_token":"fresh-for-{{posted}}","refresh_token":"next-{{posted}}","expires_in":14400,"scope":["user:write:chat"],"token_type":"bearer"}""";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class WireClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
