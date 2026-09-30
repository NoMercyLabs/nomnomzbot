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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Gdpr;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// What a Twitch token-endpoint answer does to the connection's health, over the REAL vault and crypto: an
/// authenticated success (a refresh Twitch accepted) is the one thing that clears the failure state.
/// </summary>
public sealed class TwitchAuthServiceRefreshOutcomeTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199e000-0000-7000-8000-0000000000c1");

    [Fact]
    public async Task A_refresh_twitch_accepts_clears_the_failure_count_and_the_error_stamp()
    {
        ScriptedTokenHandler wire = new();
        wire.Enqueue(HttpStatusCode.OK, IssuedPair);
        Harness harness = await Harness.BuildAsync(wire);
        await harness.Vault.MarkRefreshFailureAsync(harness.ConnectionId, "invalid_grant");
        await harness.Vault.MarkRefreshFailureAsync(harness.ConnectionId, "invalid_grant");

        TokenResult? result = await harness.Service.RefreshTokenAsync(
            Broadcaster,
            AuthEnums.IntegrationProvider.Twitch
        );

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("issued-access");
        IntegrationConnection connection = await harness.ConnectionAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        connection.ConsecutiveFailureCount.Should().Be(0);
        connection.LastErrorAt.Should().BeNull();
    }

    private const string IssuedPair =
        """{"access_token":"issued-access","refresh_token":"issued-refresh","expires_in":3600,"scope":["user:read:chat"],"token_type":"bearer"}""";

    private sealed class Harness
    {
        public required TwitchAuthService Service { get; init; }
        public required IntegrationTokenVault Vault { get; init; }
        public required AuthDbContext Db { get; init; }
        public required Guid ConnectionId { get; init; }

        public async Task<IntegrationConnection> ConnectionAsync() =>
            await Db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Id == ConnectionId);

        public static async Task<Harness> BuildAsync(HttpMessageHandler wire)
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
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.Twitch,
                    ProviderAccountId: "twitch-streamer",
                    ProviderAccountName: "streamer",
                    Scopes: ["user:read:chat"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new("old-access", "old-refresh", null, DateTime.UtcNow.AddMinutes(-1))
            );

            ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
                db,
                protector,
                new ConfigurationBuilder().Build()
            );
            TwitchAuthService service = new(
                db,
                vault,
                credentials,
                new SingleClientFactory(wire),
                NullLogger<TwitchAuthService>.Instance,
                TimeProvider.System,
                new ConnectionRefreshGate()
            );

            return new()
            {
                Service = service,
                Vault = vault,
                Db = db,
                ConnectionId = upsert.Value.Id,
            };
        }
    }

    /// <summary>Answers each token-endpoint POST with the next scripted response, and counts the POSTs.</summary>
    private sealed class ScriptedTokenHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _script = new();

        public int CallCount { get; private set; }

        public void Enqueue(HttpStatusCode status, string body) =>
            _script.Enqueue(() =>
                new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );

        public void EnqueueThrow(Exception exception) => _script.Enqueue(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(_script.Dequeue()());
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
