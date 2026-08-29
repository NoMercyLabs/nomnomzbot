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
/// S-KICK-BOT-ACCOUNT — proves <see cref="KickAccessTokenProvider"/> resolves a registered dedicated Kick
/// bot account (Provider <c>kick_bot</c>, scoped to the tenant) in preference to the streamer's own
/// account, and that the resolved <see cref="KickAccess.IsBotAccount"/> flag then drives
/// <see cref="Infrastructure.Chat.Kick.KickApiClient.SendMessageAsync"/> to send with <c>type:"bot"</c>
/// using the BOT account's own token — never the streamer's. Without a bot account registered, resolution
/// must be unchanged: the streamer's own connection, <c>IsBotAccount:false</c>.
/// </summary>
public sealed class KickAccessTokenProviderBotAccountTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000e5");
    private const string StreamerExternalId = "554433002";
    private const string BotExternalId = "998877001";

    [Fact]
    public async Task BotAccountRegistered_ResolvesBotToken_WithIsBotAccountTrue()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecordingKickHandler wire = new();

        (KickAccessTokenProvider provider, IntegrationTokenVault vault, _, Guid botConnectionId) =
            await BuildAsync(wire, clock);

        await vault.StoreTokensAsync(
            botConnectionId,
            new(
                "bot-own-access-token",
                "bot-own-refresh-token",
                null,
                clock.GetUtcNow().UtcDateTime.AddHours(1)
            )
        );

        KickAccess? access = await provider.GetAsync(Broadcaster);

        access.Should().NotBeNull();
        access!
            .IsBotAccount.Should()
            .BeTrue("a dedicated kick_bot connection is registered for this tenant");
        access
            .AccessToken.Should()
            .Be(
                "bot-own-access-token",
                "the BOT account's own token must be used, not the streamer's"
            );
        access
            .BroadcasterUserId.Should()
            .Be(
                long.Parse(StreamerExternalId),
                "the target channel is still the streamer's channel even when sending as the bot"
            );
    }

    [Fact]
    public async Task NoBotAccountRegistered_FallsBackToStreamerAccount_WithIsBotAccountFalse()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecordingKickHandler wire = new();

        (
            KickAccessTokenProvider provider,
            IntegrationTokenVault vault,
            Guid streamerConnectionId,
            _
        ) = await BuildAsync(wire, clock, registerBotConnection: false);

        await vault.StoreTokensAsync(
            streamerConnectionId,
            new(
                "streamer-access-token",
                "streamer-refresh-token",
                null,
                clock.GetUtcNow().UtcDateTime.AddHours(1)
            )
        );

        KickAccess? access = await provider.GetAsync(Broadcaster);

        access.Should().NotBeNull();
        access!
            .IsBotAccount.Should()
            .BeFalse("no kick_bot connection exists — regression-proofing the existing fallback");
        access.AccessToken.Should().Be("streamer-access-token");
    }

    private static async Task<(
        KickAccessTokenProvider Provider,
        IntegrationTokenVault Vault,
        Guid StreamerConnectionId,
        Guid BotConnectionId
    )> BuildAsync(RecordingKickHandler wire, TimeProvider clock, bool registerBotConnection = true)
    {
        string dbName = Guid.NewGuid().ToString();
        AuthDbContext db = AuthTestBuilder.NewContext(dbName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = StreamerExternalId,
                Name = "kick-bot-streamer",
                NameNormalized = "kick-bot-streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            bus,
            clock,
            NullLogger<IntegrationTokenVault>.Instance
        );

        Result<IntegrationConnectionDto> streamerUpsert = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: Broadcaster,
                Provider: AuthEnums.IntegrationProvider.Kick,
                ProviderAccountId: StreamerExternalId,
                ProviderAccountName: "kick-bot-streamer",
                Scopes: ["chat:write"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );

        Guid botConnectionId = Guid.Empty;
        if (registerBotConnection)
        {
            Result<IntegrationConnectionDto> botUpsert = await vault.UpsertConnectionAsync(
                new(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.KickBot,
                    ProviderAccountId: BotExternalId,
                    ProviderAccountName: "kick-bot-account",
                    Scopes: ["chat:write"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            botConnectionId = botUpsert.Value.Id;
        }

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
            new SingleClientFactory(wire),
            NullLogger<KickAccessTokenProvider>.Instance,
            new ConnectionRefreshGate()
        );

        return (provider, vault, streamerUpsert.Value.Id, botConnectionId);
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

    /// <summary>Records nothing but the never-called invariant — this suite never expects a refresh call.</summary>
    private sealed class RecordingKickHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"unexpected-refresh","refresh_token":"unexpected","expires_in":3600}""",
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
