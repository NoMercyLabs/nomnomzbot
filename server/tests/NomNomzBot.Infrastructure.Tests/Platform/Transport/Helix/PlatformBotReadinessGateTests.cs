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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the platform-bot readiness gate reads the SAME canonical fact a bot-scoped Twitch call depends on —
/// a usable, decryptable platform bot token in the token vault — by running the REAL resolver + vault over the
/// auth test context. On a fresh, un-onboarded install (no bot connection) it is closed; once a platform bot
/// connection with a stored token exists it is open. This is exactly the predicate that keeps the EventSub
/// transport, IRC, and the Helix warmers dormant until onboarding, then activates them.
/// </summary>
public sealed class PlatformBotReadinessGateTests
{
    private const string BotProvider = "twitch_bot";

    private static (PlatformBotReadinessGate Gate, IntegrationTokenVault Vault) Build()
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
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        TwitchTokenResolver resolver = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            new RecordingEventBus()
        );
        return (new PlatformBotReadinessGate(resolver), vault);
    }

    [Fact]
    public async Task IsConfigured_WithNoPlatformBotToken_IsFalse()
    {
        (PlatformBotReadinessGate gate, _) = Build();

        bool configured = await gate.IsPlatformBotConfiguredAsync();

        configured.Should().BeFalse("a fresh, un-onboarded install has no platform bot connection");
    }

    [Fact]
    public async Task IsConfigured_OnceThePlatformBotTokenExists_IsTrue()
    {
        (PlatformBotReadinessGate gate, IntegrationTokenVault vault) = Build();

        // Closed before onboarding.
        (await gate.IsPlatformBotConfiguredAsync())
            .Should()
            .BeFalse();

        // Authorizing the bot (vaulting its connection + token) is exactly what onboarding does.
        await StoreBotConnectionAsync(vault, "bot-access-PLAINTEXT", "bot-user-1");

        (await gate.IsPlatformBotConfiguredAsync())
            .Should()
            .BeTrue("the platform bot is authorized and its token decrypts");
    }

    /// <summary>
    /// Self-host two-account model: with no dedicated bot account, the streamer's own main-account token is the
    /// bot identity, so the gate must report ready off the owner's <c>twitch</c> connection alone — this is what
    /// un-defers the action-required chat notice on a fresh self-host install (no bot account ever registered).
    /// </summary>
    [Fact]
    public async Task IsConfigured_WithOnlyTheOwnersOwnUserToken_IsTrue()
    {
        (PlatformBotReadinessGate gate, IntegrationTokenVault vault) = Build();
        Guid owner = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");

        await StoreOwnerConnectionAsync(vault, owner, "owner-access-PLAINTEXT", "owner-user-1");

        (await gate.IsPlatformBotConfiguredAsync())
            .Should()
            .BeTrue("the main account is the bot until a custom bot account is registered");
    }

    private static async Task StoreBotConnectionAsync(
        IntegrationTokenVault vault,
        string accessToken,
        string accountId
    ) =>
        await StoreConnectionAsync(vault, broadcasterId: null, BotProvider, accessToken, accountId);

    private static async Task StoreOwnerConnectionAsync(
        IntegrationTokenVault vault,
        Guid owner,
        string accessToken,
        string accountId
    ) =>
        await StoreConnectionAsync(
            vault,
            owner,
            AuthEnums.IntegrationProvider.Twitch,
            accessToken,
            accountId
        );

    private static async Task StoreConnectionAsync(
        IntegrationTokenVault vault,
        Guid? broadcasterId,
        string provider,
        string accessToken,
        string accountId
    )
    {
        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    broadcasterId,
                    provider,
                    accountId,
                    "login",
                    [],
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
            []
        );
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for these gate tests.</summary>
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
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>(actualScopes));
    }
}
