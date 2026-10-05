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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The proactive sweep (<see cref="TwitchAuthService.RefreshExpiringTokensAsync"/>) runs once per timer
/// interval. A token that expires before the NEXT tick must be refreshed on THIS tick, or one late or
/// failed tick lets it lapse: the refresh window has to be wider than the interval. Proven over the real
/// vault and real envelope crypto; assertions are the tokens the vault holds afterwards.
/// </summary>
public sealed class TwitchAuthServiceExpiringTokenSweepTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199c000-0000-7000-8000-0000000000c1");

    [Fact]
    public async Task RefreshExpiringTokensAsync_refreshes_a_token_with_45_minutes_left_and_saves_the_new_pair()
    {
        (
            TwitchAuthService service,
            IntegrationTokenVault vault,
            Guid connectionId,
            FixedTokenHandler wire
        ) = await BuildAsync(expiresIn: TimeSpan.FromMinutes(45));

        await service.RefreshExpiringTokensAsync();

        wire.CallCount.Should().Be(1);
        Result<DecryptedTokenDto> access = await vault.GetAccessTokenAsync(connectionId);
        access.IsSuccess.Should().BeTrue(access.ErrorMessage);
        access.Value.Value.Should().Be("issued-access");
        access
            .Value.ExpiresAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefreshExpiringTokensAsync_leaves_a_token_with_more_than_two_intervals_left_alone()
    {
        (
            TwitchAuthService service,
            IntegrationTokenVault vault,
            Guid connectionId,
            FixedTokenHandler wire
        ) = await BuildAsync(expiresIn: TimeSpan.FromMinutes(90));

        await service.RefreshExpiringTokensAsync();

        wire.CallCount.Should().Be(0);
        Result<DecryptedTokenDto> access = await vault.GetAccessTokenAsync(connectionId);
        access.Value.Value.Should().Be("old-access");
    }

    private static async Task<(
        TwitchAuthService Service,
        IntegrationTokenVault Vault,
        Guid ConnectionId,
        FixedTokenHandler Wire
    )> BuildAsync(TimeSpan expiresIn)
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
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: Broadcaster,
                Provider: AuthEnums.IntegrationProvider.Twitch,
                ProviderAccountId: $"twitch-{Broadcaster}",
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
            new("old-access", "old-refresh", null, DateTime.UtcNow.Add(expiresIn)),
            grantedScopes: null
        );

        FixedTokenHandler wire = new();
        TwitchAuthService service = new(
            db,
            vault,
            AuthTestBuilder.CredentialsProvider(db, protector, new ConfigurationBuilder().Build()),
            new SingleClientFactory(wire),
            NullLogger<TwitchAuthService>.Instance,
            TimeProvider.System,
            new ConnectionRefreshGate()
        );
        return (service, vault, upsert.Value.Id, wire);
    }

    private sealed class PassthroughScopeGrant : IScopeGrantService
    {
        public IReadOnlyList<string> RequiredScopesFor(string featureKey) => [];

        public Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
            Guid broadcasterId,
            string featureKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScopeGrantState(true, null, [])));

        public Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
            Guid connectionId,
            IReadOnlyList<string> actualScopes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));
    }

    /// <summary>Counts every POST to the token endpoint and answers with one fixed token pair (1 hour).</summary>
    private sealed class FixedTokenHandler : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"issued-access","refresh_token":"issued-refresh","expires_in":3600,"scope":["user:read:chat"],"token_type":"bearer"}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
