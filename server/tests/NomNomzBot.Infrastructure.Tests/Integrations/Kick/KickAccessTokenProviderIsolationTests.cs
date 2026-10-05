// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.Kick;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Integrations.Kick;

/// <summary>
/// Tenant isolation for the Kick streamer-account fallback: the vaulted <c>kick</c> connection is looked up
/// by the channel's own numeric account id, and that lookup must stay inside the channel — a row carrying
/// the same account id under ANOTHER channel is never returned. The identity-plane login row
/// (<c>BroadcasterId</c> null) remains an allowed fallback, ranked below the channel-scoped grant.
/// </summary>
public sealed class KickAccessTokenProviderIsolationTests
{
    private static readonly Guid ChannelA = Guid.Parse("0199d000-0000-7000-8000-0000000000a1");
    private static readonly Guid ChannelB = Guid.Parse("0199d000-0000-7000-8000-0000000000b2");
    private const string SharedExternalId = "554433002";

    [Fact]
    public async Task GetAsync_NeverReturnsTheSameAccountsRowVaultedUnderAnotherChannel()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (KickAccessTokenProvider provider, IntegrationTokenVault vault) = await BuildAsync(clock);
        Guid otherChannelsRow = await UpsertAsync(vault, ChannelB);
        await vault.StoreTokensAsync(
            otherChannelsRow,
            new(
                "channel-b-access-token",
                "channel-b-refresh-token",
                null,
                clock.GetUtcNow().UtcDateTime.AddHours(1)
            )
        );

        KickAccess? access = await provider.GetAsync(ChannelA);

        access
            .Should()
            .BeNull("channel A has no Kick grant of its own; channel B's row is not A's");
    }

    [Fact]
    public async Task GetAsync_PrefersTheChannelScopedRow_OverTheLoginOnlyRow()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (KickAccessTokenProvider provider, IntegrationTokenVault vault) = await BuildAsync(clock);
        Guid loginRow = await UpsertAsync(vault, null);
        Guid scopedRow = await UpsertAsync(vault, ChannelA);
        DateTime expires = clock.GetUtcNow().UtcDateTime.AddHours(1);
        await vault.StoreTokensAsync(
            loginRow,
            new("login-access-token", "login-refresh", null, expires)
        );
        await vault.StoreTokensAsync(
            scopedRow,
            new("scoped-access-token", "scoped-refresh", null, expires)
        );

        KickAccess? access = await provider.GetAsync(ChannelA);

        access.Should().NotBeNull();
        access.AccessToken.Should().Be("scoped-access-token");
        access.IsBotAccount.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_FallsBackToTheLoginOnlyRow_WhenTheChannelHasNoScopedGrant()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (KickAccessTokenProvider provider, IntegrationTokenVault vault) = await BuildAsync(clock);
        Guid loginRow = await UpsertAsync(vault, null);
        await vault.StoreTokensAsync(
            loginRow,
            new(
                "login-access-token",
                "login-refresh",
                null,
                clock.GetUtcNow().UtcDateTime.AddHours(1)
            )
        );

        KickAccess? access = await provider.GetAsync(ChannelA);

        access.Should().NotBeNull();
        access.AccessToken.Should().Be("login-access-token");
    }

    private static async Task<Guid> UpsertAsync(IntegrationTokenVault vault, Guid? broadcasterId)
    {
        Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: broadcasterId,
                Provider: AuthEnums.IntegrationProvider.Kick,
                ProviderAccountId: SharedExternalId,
                ProviderAccountName: "kick-streamer",
                Scopes: ["chat:write"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );
        upsert.IsSuccess.Should().BeTrue(upsert.ErrorMessage);
        return upsert.Value.Id;
    }

    private static async Task<(
        KickAccessTokenProvider Provider,
        IntegrationTokenVault Vault
    )> BuildAsync(TimeProvider clock)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        foreach ((Guid id, string name) in new[] { (ChannelA, "kick-a"), (ChannelB, "kick-b") })
        {
            db.Channels.Add(
                new()
                {
                    Id = id,
                    Provider = AuthEnums.Platform.Kick,
                    ExternalChannelId = SharedExternalId,
                    Name = name,
                    NameNormalized = name,
                    OwnerUserId = Guid.NewGuid(),
                }
            );
        }
        await db.SaveChangesAsync();

        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            clock,
            NullLogger<IntegrationTokenVault>.Instance
        );
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            new ConfigurationBuilder().Build()
        );
        KickAccessTokenProvider provider = new(
            db,
            vault,
            credentials,
            clock,
            new NeverCalledClientFactory(),
            NullLogger<KickAccessTokenProvider>.Instance,
            new ConnectionRefreshGate()
        );
        return (provider, vault);
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

    /// <summary>No token in this suite is near expiry, so a refresh call is a test failure by itself.</summary>
    private sealed class NeverCalledClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ThrowingHandler(), disposeHandler: false);

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) => throw new InvalidOperationException("No Kick refresh is expected in this suite.");
        }
    }
}
