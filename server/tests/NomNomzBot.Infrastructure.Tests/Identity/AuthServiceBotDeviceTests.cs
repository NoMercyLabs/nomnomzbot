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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Deployment;
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

    // ─── scaffolding ───────────────────────────────────────────────────────────

    // Only the credentials + device-code service are load-bearing for these two methods (neither writes the DB),
    // so the rest of AuthService's collaborators are inert substitutes — a reach into them would fail the build.
    private static AuthService Build(IConfiguration config, ITwitchDeviceCodeService deviceCode)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new AuthService(
            db,
            Substitute.For<ITwitchAuthService>(),
            deviceCode,
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new DeploymentContext(DeploymentMode.SelfHostLite),
            TimeProvider.System,
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
