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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the Helix bearer resolver reads the canonical token vault — the same store login/refresh write —
/// not the retired flat <c>Service</c> table: a tenant's vaulted user token round-trips back out, a tenant
/// with no user connection falls back to the platform bot token, the granted-scope check reads
/// <c>IntegrationConnection.Scopes</c>, and a tenant with no connection at all fails with <c>no_token</c>.
/// (These fail against the old Service-reading resolver, which is the regression they guard.)
/// </summary>
public sealed class TwitchTokenResolverTests
{
    private const string BotProvider = "twitch_bot";
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000f7");

    private static (
        TwitchTokenResolver Resolver,
        IntegrationTokenVault Vault,
        AuthDbContext Db
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new NoopScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        TwitchTokenResolver resolver = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchAppTokenProvider>(),
            new RecordingEventBus()
        );
        return (resolver, vault, db);
    }

    private static async Task StoreConnectionAsync(
        IntegrationTokenVault vault,
        Guid? broadcasterId,
        string provider,
        string accessToken,
        string accountId,
        params string[] scopes
    )
    {
        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    broadcasterId,
                    provider,
                    accountId,
                    "login",
                    scopes,
                    ClientId: "client",
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            )
        )
            .Value
            .Id;

        await vault.StoreTokensAsync(
            connectionId,
            new StoreTokensDto(accessToken, "refresh", AppToken: null, DateTime.UtcNow.AddHours(1)),
            scopes
        );
    }

    [Fact]
    public async Task GetBroadcasterToken_ReturnsTheVaultedAccessToken_ForTheTenantConnection()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(
            vault,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "broadcaster-access-PLAINTEXT",
            "twitch-user-1"
        );

        Result<TwitchAccessContext> result = await resolver.GetBroadcasterTokenAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("broadcaster-access-PLAINTEXT");
        result.Value.BroadcasterId.Should().Be(Tenant);
        result.Value.ServiceName.Should().Be(AuthEnums.IntegrationProvider.Twitch);
    }

    [Fact]
    public async Task GetBroadcasterToken_WithNoUserConnection_FallsBackToThePlatformBotToken()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(vault, null, BotProvider, "bot-access-PLAINTEXT", "bot-user-1");

        Result<TwitchAccessContext> result = await resolver.GetBroadcasterTokenAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("bot-access-PLAINTEXT");
        result.Value.BroadcasterId.Should().BeNull();
        result.Value.ServiceName.Should().Be(BotProvider);
    }

    [Fact]
    public async Task GetBotToken_WithRegisteredBotAccount_ReturnsThePlatformBotToken()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(vault, null, BotProvider, "bot-access-PLAINTEXT", "bot-user-1");

        Result<TwitchAccessContext> result = await resolver.GetBotTokenAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("bot-access-PLAINTEXT");
        result.Value.ServiceName.Should().Be(BotProvider);
    }

    /// <summary>
    /// Self-host two-account model (onboarding.md): before any bot account is registered, the bot's chat
    /// identity IS the owner's own main account. With no <c>twitch_bot</c> connection, <c>GetBotTokenAsync</c>
    /// must fall back to the single owner's <c>twitch</c> user token — not fail <c>no_token</c>, which is what
    /// stranded the readiness gate and the deferred chat notice on a fresh self-host install.
    /// </summary>
    [Fact]
    public async Task GetBotToken_WithNoBotAccount_FallsBackToTheOwnersOwnUserToken()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(
            vault,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "owner-access-PLAINTEXT",
            "owner-user-1"
        );

        Result<TwitchAccessContext> result = await resolver.GetBotTokenAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("owner-access-PLAINTEXT");
        result.Value.BroadcasterId.Should().Be(Tenant);
        result.Value.ServiceName.Should().Be(AuthEnums.IntegrationProvider.Twitch);
    }

    /// <summary>
    /// A registered bot account takes precedence over the owner's main account: once a <c>twitch_bot</c>
    /// connection exists, the bot speaks as the bot — never the owner — even though the owner connection is
    /// also present. Guards the resolution order (bot-account first, owner-fallback only when absent).
    /// </summary>
    [Fact]
    public async Task GetBotToken_WithBothBotAndOwner_PrefersTheRegisteredBotAccount()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(
            vault,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "owner-access-PLAINTEXT",
            "owner-user-1"
        );
        await StoreConnectionAsync(vault, null, BotProvider, "bot-access-PLAINTEXT", "bot-user-1");

        Result<TwitchAccessContext> result = await resolver.GetBotTokenAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("bot-access-PLAINTEXT");
        result.Value.BroadcasterId.Should().BeNull();
        result.Value.ServiceName.Should().Be(BotProvider);
    }

    /// <summary>
    /// A fresh, un-onboarded self-host install (no bot account, no owner connection) still fails <c>no_token</c>
    /// — the fallback adds a path, it does not paper over a wholly unconfigured system.
    /// </summary>
    [Fact]
    public async Task GetBotToken_WithNoConnectionAtAll_FailsWithNoToken()
    {
        (TwitchTokenResolver resolver, _, _) = Build();

        Result<TwitchAccessContext> result = await resolver.GetBotTokenAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NoToken);
    }

    [Fact]
    public async Task GetBroadcasterToken_WhenNoConnectionAtAll_FailsWithNoToken()
    {
        (TwitchTokenResolver resolver, _, _) = Build();

        Result<TwitchAccessContext> result = await resolver.GetBroadcasterTokenAsync(Tenant);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NoToken);
    }

    [Fact]
    public async Task HasScope_ReadsTheConnectionGrantedScopes()
    {
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, _) = Build();
        await StoreConnectionAsync(
            vault,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "access",
            "twitch-user-1",
            "channel:read:subscriptions"
        );

        (await resolver.HasScopeAsync(Tenant, "channel:read:subscriptions")).Should().BeTrue();
        (await resolver.HasScopeAsync(Tenant, "channel:manage:raids")).Should().BeFalse();
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for these resolver tests.</summary>
    private sealed class NoopScopeGrant : IScopeGrantService
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
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>(actualScopes));
    }
}
