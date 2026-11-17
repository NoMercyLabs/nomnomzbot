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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Music;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A channel's own white-label bot speaks its chat lines. The real <see cref="HelixChatProvider"/>,
/// <see cref="TwitchHelixTransport"/> and <see cref="TwitchTokenResolver"/> run over a real token vault; only
/// the wire is faked, so the test reads the <c>sender_id</c> and the bearer that actually leave the process.
/// </summary>
public sealed class HelixChatProviderCustomBotTests
{
    private static readonly Guid CustomChannel = Guid.Parse("0197c000-0000-7000-8000-0000000000c1");
    private static readonly Guid SharedChannel = Guid.Parse("0197c000-0000-7000-8000-0000000000c2");
    private const string CustomChannelTwitchId = "channel-c1";
    private const string SharedChannelTwitchId = "channel-c2";
    private const string CustomBotUserId = "custom-bot-501";
    private const string CustomBotToken = "custom-bot-token";
    private const string SharedBotUserId = "shared-bot-900";
    private const string SharedBotToken = "shared-bot-token";

    [Fact]
    public async Task A_channel_with_its_own_bot_sends_as_that_bot_and_others_still_send_as_the_shared_bot()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IIntegrationTokenVault vault = NewVault(db);
        await ConnectAsync(vault, null, "twitch_bot", SharedBotUserId, SharedBotToken);
        await ConnectOwnerAsync(vault, CustomChannel, "owner-c1");
        await ConnectOwnerAsync(vault, SharedChannel, "owner-c2");
        Guid customConnection = await ConnectAsync(
            vault,
            CustomChannel,
            "twitch_bot",
            CustomBotUserId,
            CustomBotToken
        );
        await AuthorizeCustomBotAsync(db, CustomChannel, customConnection);

        (HelixChatProvider provider, RecordingHelixHandler wire) = Build(db, vault);

        bool customSent = await provider.SendMessageAsync(CustomChannel, "from my own bot");
        bool sharedSent = await provider.SendMessageAsync(SharedChannel, "from the shared bot");

        customSent.Should().BeTrue();
        sharedSent.Should().BeTrue();
        wire.Requests.Should().HaveCount(2);

        RecordedRequest custom = wire.Requests[0];
        custom.Uri.AbsolutePath.Should().Be("/helix/chat/messages");
        SenderOf(custom)
            .Should()
            .Be(CustomBotUserId, "a channel's connected white-label bot is the chat sender");
        BroadcasterOf(custom).Should().Be(CustomChannelTwitchId);
        custom
            .AuthorizationParameter.Should()
            .Be(CustomBotToken, "the send is signed by the white-label bot's own token");

        RecordedRequest shared = wire.Requests[1];
        SenderOf(shared)
            .Should()
            .Be(SharedBotUserId, "a channel without its own bot keeps the shared bot");
        BroadcasterOf(shared).Should().Be(SharedChannelTwitchId);
        shared.AuthorizationParameter.Should().Be(SharedBotToken);
    }

    [Fact]
    public async Task A_deactivated_own_bot_no_longer_speaks_and_the_shared_bot_takes_over()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IIntegrationTokenVault vault = NewVault(db);
        await ConnectAsync(vault, null, "twitch_bot", SharedBotUserId, SharedBotToken);
        await ConnectOwnerAsync(vault, CustomChannel, "owner-c1");
        Guid customConnection = await ConnectAsync(
            vault,
            CustomChannel,
            "twitch_bot",
            CustomBotUserId,
            CustomBotToken
        );
        ChannelBotAuthorization authorization = await AuthorizeCustomBotAsync(
            db,
            CustomChannel,
            customConnection
        );
        authorization.IsActive = false;
        await db.SaveChangesAsync();

        (HelixChatProvider provider, RecordingHelixHandler wire) = Build(db, vault);

        bool sent = await provider.SendMessageAsync(CustomChannel, "after disconnect");

        sent.Should().BeTrue();
        RecordedRequest request = wire.Requests.Should().ContainSingle().Subject;
        SenderOf(request).Should().Be(SharedBotUserId);
        request.AuthorizationParameter.Should().Be(SharedBotToken);
    }

    private static (HelixChatProvider Provider, RecordingHelixHandler Wire) Build(
        AuthDbContext db,
        IIntegrationTokenVault vault
    )
    {
        RecordingHelixHandler wire = new([]);
        IOptions<TwitchOptions> options = Options.Create(
            new TwitchOptions
            {
                ClientId = "test-client",
                ClientSecret = "s",
                BotUsername = "b",
            }
        );
        HttpClient http = new(new TwitchAuthHeaderHandler(options) { InnerHandler = wire });
        TwitchTokenResolver tokens = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchAppTokenProvider>(),
            new RecordingEventBus(),
            Substitute.For<IDeploymentProfileService>()
        );
        OutboundSanctionAccessor sanctions = new();
        TwitchHelixTransport transport = new(
            new SingleClientFactory(http),
            tokens,
            NullSystemCredentialsProvider.Instance,
            new CapturingEventBus(),
            sanctions,
            NullLogger<TwitchHelixTransport>.Instance
        );

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(CustomChannel, Arg.Any<CancellationToken>())
            .Returns(CustomChannelTwitchId);
        identity
            .GetTwitchChannelIdAsync(SharedChannel, Arg.Any<CancellationToken>())
            .Returns(SharedChannelTwitchId);

        HelixChatProvider provider = new(
            transport,
            Substitute.For<ITwitchModerationApi>(),
            identity,
            db,
            new HelixBadgeSendGate(TimeProvider.System),
            options,
            NullLogger<HelixChatProvider>.Instance,
            sanctions,
            new BotChatSendRefusalReporter(Substitute.For<IEventBus>(), TimeProvider.System)
        );
        return (provider, wire);
    }

    private static IIntegrationTokenVault NewVault(AuthDbContext db)
    {
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        return new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
    }

    private static Task<Guid> ConnectOwnerAsync(
        IIntegrationTokenVault vault,
        Guid channel,
        string ownerUserId
    ) =>
        ConnectAsync(
            vault,
            channel,
            AuthEnums.IntegrationProvider.Twitch,
            ownerUserId,
            $"{ownerUserId}-token"
        );

    private static async Task<Guid> ConnectAsync(
        IIntegrationTokenVault vault,
        Guid? broadcasterId,
        string provider,
        string accountId,
        string accessToken
    )
    {
        string[] scopes = ["user:write:chat", "user:read:chat"];
        Result<IntegrationConnectionDto> connection = await vault.UpsertConnectionAsync(
            new(
                broadcasterId,
                provider,
                accountId,
                accountId,
                scopes,
                "test-client",
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );
        connection.IsSuccess.Should().BeTrue();
        Result stored = await vault.StoreTokensAsync(
            connection.Value.Id,
            new(accessToken, "refresh", AppToken: null, DateTime.UtcNow.AddHours(4)),
            scopes
        );
        stored.IsSuccess.Should().BeTrue();
        return connection.Value.Id;
    }

    private static async Task<ChannelBotAuthorization> AuthorizeCustomBotAsync(
        AuthDbContext db,
        Guid channel,
        Guid connectionId
    )
    {
        BotAccount bot = new()
        {
            IdentityType = AuthEnums.BotIdentityType.Custom,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = CustomBotUserId,
            BotUsername = "custom_bot",
            ConnectionId = connectionId,
            IsActive = true,
        };
        ChannelBotAuthorization authorization = new()
        {
            BroadcasterId = channel,
            BotAccountId = bot.Id,
            AuthorizedAt = DateTime.UtcNow,
            IsActive = true,
        };
        db.BotAccounts.Add(bot);
        db.ChannelBotAuthorizations.Add(authorization);
        await db.SaveChangesAsync();
        return authorization;
    }

    private static string? SenderOf(RecordedRequest request) => Field(request, "sender_id");

    private static string? BroadcasterOf(RecordedRequest request) =>
        Field(request, "broadcaster_id");

    private static string? Field(RecordedRequest request, string name)
    {
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty(name).GetString();
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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
        ) => Task.FromResult(Result.Success(actualScopes));
    }
}
