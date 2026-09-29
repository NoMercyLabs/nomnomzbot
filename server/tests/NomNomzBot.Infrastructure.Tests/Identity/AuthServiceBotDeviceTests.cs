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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the bot-account connect works WITHOUT a Twitch client secret. The redirect (authorization-code) bot
/// URL requires a secret, so it fails <c>TWITCH_NOT_CONFIGURED</c> on a secret-free client — that is the 400 the
/// dashboard's bot connect used to hit. The secret-free path is the bot Device Code Flow: it mints a user/device
/// code from the client id alone (the shipped public id or a BYOC override). A secret is purely the enhancement
/// that re-enables the redirect bot flow — it is never required to connect the bot.
/// </summary>
public sealed class AuthServiceBotDeviceTests
{
    [Fact]
    public async Task StartBotDeviceLoginAsync_MintsADeviceCode_WithOnlyAClientId_AndNoSecret()
    {
        // The keystone: a client id alone (no secret) yields a device code the dashboard shows — never a 400.
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .RequestDeviceCodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                new DeviceCodeResult(
                    "DEV-BOT-1",
                    "WXYZ-7890",
                    "https://www.twitch.tv/activate",
                    5,
                    DateTime.UtcNow.AddMinutes(30)
                )
            );

        AuthService service = Build(ConfigWith(clientId: "public-id", secret: null), deviceCode);

        Result<DeviceCodeStartDto> result = await service.StartBotDeviceLoginAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.UserCode.Should().Be("WXYZ-7890");
        result.Value.DeviceCode.Should().Be("DEV-BOT-1");
        result.Value.VerificationUri.Should().Be("https://www.twitch.tv/activate");

        // The bot device login requests the bot chat scopes — never a streamer/login scope set.
        await deviceCode
            .Received(1)
            .RequestDeviceCodeAsync(
                Arg.Is<IReadOnlyList<string>>(s =>
                    s.Contains("user:write:chat") && s.Contains("user:read:chat")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task StartBotDeviceLoginAsync_FailsCleanly_WhenNoClientIdIsConfiguredAtAll()
    {
        // No id anywhere: the transport returns null (it never calls Twitch), surfaced as TWITCH_NOT_CONFIGURED.
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .RequestDeviceCodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((DeviceCodeResult?)null);

        AuthService service = Build(new ConfigurationBuilder().Build(), deviceCode);

        Result<DeviceCodeStartDto> result = await service.StartBotDeviceLoginAsync();

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TWITCH_NOT_CONFIGURED");
    }

    [Fact]
    public async Task GetTwitchBotOAuthUrl_RequiresASecret_SoItIsTheEnhancementNotTheDefault()
    {
        // The redirect bot flow is gated on the FULL credential set (a secret). Secret-free, it fails the same
        // way the dashboard's old bot connect did — which is exactly why connect now falls back to the device
        // flow above when no secret is configured.
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        AuthService secretFree = Build(ConfigWith(clientId: "public-id", secret: null), deviceCode);

        Result<string> noSecret = await secretFree.GetTwitchBotOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        noSecret.IsFailure.Should().BeTrue();
        noSecret.ErrorCode.Should().Be("TWITCH_NOT_CONFIGURED");

        // With a secret configured, the redirect bot URL builds — the enhancement is available.
        AuthService withSecret = Build(
            ConfigWith(clientId: "public-id", secret: "shh"),
            deviceCode
        );

        Result<string> redirect = await withSecret.GetTwitchBotOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        redirect.IsSuccess.Should().BeTrue();
        redirect.Value.Should().StartWith("https://id.twitch.tv/oauth2/authorize");
        redirect.Value.Should().Contain("client_id=public-id");
    }

    // ─── a channel's own bot is a plan feature ─────────────────────────────────

    [Fact]
    public async Task GetTwitchChannelBotOAuthUrl_IsRefused_WhenThePlanExcludesAnOwnBot_AndBuilds_WhenItIncludesOne()
    {
        Guid channel = Guid.NewGuid();
        IConfiguration config = ConfigWith(clientId: "public-id", secret: "shh");
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();

        AuthService excluded = Build(config, deviceCode, Plan(channel, allowsOwnBot: false));
        Result<string> refused = await excluded.GetTwitchChannelBotOAuthUrl(
            channel,
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        refused.IsFailure.Should().BeTrue();
        refused.ErrorCode.Should().Be("NOT_ENTITLED");

        AuthService included = Build(config, deviceCode, Plan(channel, allowsOwnBot: true));
        Result<string> built = await included.GetTwitchChannelBotOAuthUrl(
            channel,
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        built.IsSuccess.Should().BeTrue(built.ErrorMessage);
        built.Value.Should().StartWith("https://id.twitch.tv/oauth2/authorize");
    }

    [Fact]
    public async Task HandleTwitchChannelBotCallback_IsRefused_BeforeTheCodeIsExchanged_WhenThePlanExcludesAnOwnBot()
    {
        Guid channel = Guid.NewGuid();
        ITwitchAuthService twitchAuth = Substitute.For<ITwitchAuthService>();
        AuthService service = Build(
            ConfigWith(clientId: "public-id", secret: "shh"),
            Substitute.For<ITwitchDeviceCodeService>(),
            Plan(channel, allowsOwnBot: false),
            twitchAuth
        );

        Result<BotStatusDto> result = await service.HandleTwitchChannelBotCallbackAsync(
            channel,
            new() { Code = "code-from-twitch", State = "nonce" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_ENTITLED");
        // No token is ever obtained for a channel that may not have its own bot.
        twitchAuth.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartChannelBotDeviceLogin_MintsTheBotCode_OnlyWhenThePlanIncludesAnOwnBot()
    {
        Guid channel = Guid.NewGuid();
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .RequestDeviceCodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                new DeviceCodeResult(
                    "DEV-BOT-2",
                    "ABCD-1234",
                    "https://www.twitch.tv/activate",
                    5,
                    DateTime.UtcNow.AddMinutes(30)
                )
            );
        IConfiguration config = ConfigWith(clientId: "public-id", secret: null);

        Result<DeviceCodeStartDto> refused = await Build(
                config,
                deviceCode,
                Plan(channel, allowsOwnBot: false)
            )
            .StartChannelBotDeviceLoginAsync(channel);

        refused.IsFailure.Should().BeTrue();
        refused.ErrorCode.Should().Be("NOT_ENTITLED");
        deviceCode
            .ReceivedCalls()
            .Should()
            .BeEmpty("no code is minted for a channel that may not use it");

        Result<DeviceCodeStartDto> started = await Build(
                config,
                deviceCode,
                Plan(channel, allowsOwnBot: true)
            )
            .StartChannelBotDeviceLoginAsync(channel);

        started.IsSuccess.Should().BeTrue(started.ErrorMessage);
        started.Value.DeviceCode.Should().Be("DEV-BOT-2");
        await deviceCode
            .Received(1)
            .RequestDeviceCodeAsync(
                Arg.Is<IReadOnlyList<string>>(s =>
                    s.Contains("user:write:chat") && s.Contains("user:read:chat")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PollChannelBotDeviceLogin_EndsInError_WithoutPollingTwitch_WhenThePlanExcludesAnOwnBot()
    {
        Guid channel = Guid.NewGuid();
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        AuthService service = Build(
            ConfigWith(clientId: "public-id", secret: null),
            deviceCode,
            Plan(channel, allowsOwnBot: false)
        );

        Result<DeviceBotPollDto> result = await service.PollChannelBotDeviceLoginAsync(
            channel,
            "DEV-BOT-1"
        );

        // A terminal status the poll loop stops on, never a failure it would tolerate until the code expires.
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DeviceLoginStatus.Error);
        deviceCode.ReceivedCalls().Should().BeEmpty();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static IBillingTierService Plan(Guid channel, bool allowsOwnBot)
    {
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .GetEntitlementAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EntitlementDto(
                        "base",
                        allowsOwnBot,
                        PrioritySupport: false,
                        new Dictionary<string, long>()
                    )
                )
            );
        return tiers;
    }

    // Only the credentials + device-code service (and the plan, for a channel's own bot) are load-bearing for
    // these methods (none writes the DB), so the rest of AuthService's collaborators are inert substitutes.
    private static AuthService Build(
        IConfiguration config,
        ITwitchDeviceCodeService deviceCode,
        IBillingTierService? tiers = null,
        ITwitchAuthService? twitchAuth = null
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new(
            db,
            twitchAuth ?? Substitute.For<ITwitchAuthService>(),
            deviceCode,
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            tiers ?? Substitute.For<IBillingTierService>(),
            NullLogger<AuthService>.Instance
        );
    }

    private static IConfiguration ConfigWith(string clientId, string? secret)
    {
        Dictionary<string, string?> values = new() { ["Twitch:ClientId"] = clientId };
        if (secret is not null)
            values["Twitch:ClientSecret"] = secret;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
