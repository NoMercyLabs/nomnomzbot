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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the readiness contract that makes a Twitch client secret genuinely OPTIONAL: a client id ALONE makes
/// the system ready (the bot logs in + talks via the secret-free device-code flow), and the status distinguishes
/// "ready via device-code" from "ready via redirect (secret configured)". A missing client id is still not-ready;
/// a missing secret is not. The frontend routes its onboarding off exactly these fields.
/// </summary>
public sealed class SystemControllerStatusTests
{
    [Fact]
    public async Task Status_is_ready_but_onboarding_is_not_complete_on_a_shipped_client_id_alone()
    {
        // THE REPRODUCED BUG: a virgin database resolves the shipped public client id via config fallback
        // (Ready=true, usable now), but the operator never made a decision. Onboarding must stay incomplete —
        // Ready/Status still honestly report the device-code flow is usable.
        (SystemController controller, _) = Build(
            clientId: "abc123",
            secret: null,
            botConnected: true,
            decisionRecorded: false
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeFalse();
        status.Checks.TwitchApp.Ready.Should().BeTrue();
        status.Checks.TwitchApp.Ok.Should().BeFalse(); // no secret ⇒ no redirect login, only device-code
        status.Checks.TwitchApp.Status.Should().Be("ready_device");
    }

    [Fact]
    public async Task Status_reports_redirect_mode_when_a_secret_is_configured()
    {
        // A configured secret is the enhancement: the check is now redirect-capable (Ok) on top of usable —
        // independent of whether a decision was recorded.
        (SystemController controller, _) = Build(
            clientId: "abc123",
            secret: "shh",
            botConnected: true,
            decisionRecorded: true
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeTrue();
        status.Checks.TwitchApp.Ready.Should().BeTrue();
        status.Checks.TwitchApp.Ok.Should().BeTrue();
        status.Checks.TwitchApp.Status.Should().Be("ready_redirect");
    }

    [Fact]
    public async Task Status_is_not_ready_and_twitch_is_missing_when_no_client_id_is_set()
    {
        // A missing CLIENT ID is still not-ready (a missing secret is not). The Twitch check is neither usable
        // nor redirect-capable, and reports "missing" — the only state that routes to the setup wizard.
        (SystemController controller, _) = Build(
            clientId: null,
            secret: null,
            botConnected: true,
            decisionRecorded: false
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeFalse();
        status.Checks.TwitchApp.Ready.Should().BeFalse();
        status.Checks.TwitchApp.Ok.Should().BeFalse();
        status.Checks.TwitchApp.Status.Should().Be("missing");
    }

    [Fact]
    public async Task Onboarding_is_complete_once_a_decision_is_recorded_even_with_no_bot_authorized()
    {
        // The keystone of the login-lockout fix: onboarding (OnboardingComplete) is deployment-level
        // configuration ONLY, driven by a RECORDED decision. Bot authorization is per-channel work that
        // happens after login, so it must NEVER gate reaching the login screen — even though the bot's own
        // live state is still reported, separately and honestly, in PlatformBot.
        (SystemController controller, _) = Build(
            clientId: "abc123",
            secret: null,
            botConnected: false,
            decisionRecorded: true
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeTrue();
        status.Checks.TwitchApp.Ready.Should().BeTrue();
        status.Checks.PlatformBot.Ok.Should().BeFalse();
        status.Checks.PlatformBot.Status.Should().Be("disconnected");
    }

    [Fact]
    public async Task Onboarding_is_not_complete_with_no_platform_credentials_at_all()
    {
        (SystemController controller, _) = Build(
            clientId: null,
            secret: null,
            botConnected: false,
            decisionRecorded: false
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Onboarding_is_not_complete_with_a_usable_client_id_and_bot_but_no_recorded_decision()
    {
        // Even with the bot fully authorized, a usable-but-undecided client id must not complete onboarding —
        // the decision is the ONLY thing that counts, never a side effect of other state being ready.
        (SystemController controller, _) = Build(
            clientId: "abc123",
            secret: null,
            botConnected: true,
            decisionRecorded: false
        );

        SystemController.SystemStatusDto status = await ReadStatus(controller);

        status.OnboardingComplete.Should().BeFalse();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static async Task<SystemController.SystemStatusDto> ReadStatus(
        SystemController controller
    )
    {
        IActionResult result = await controller.GetStatus(default);
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<SystemController.SystemStatusDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<SystemController.SystemStatusDto>>()
            .Subject;
        return body.Data!;
    }

    private static (SystemController Controller, ISystemCredentialsProvider Credentials) Build(
        string? clientId,
        string? secret,
        bool botConnected,
        bool decisionRecorded
    )
    {
        IAuthService authService = Substitute.For<IAuthService>();
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        IHostEnvironment env = Substitute.For<IHostEnvironment>();
        ITwitchOAuthStateService oauthState = Substitute.For<ITwitchOAuthStateService>();
        IConfiguration config = new ConfigurationBuilder().Build();

        env.EnvironmentName.Returns("Production");

        // GetClientIdAsync drives the device-code (Ready) signal; GetAsync (both fields) drives the redirect (Ok)
        // signal — non-null only when a secret is also present. The optional providers stay unconfigured.
        credentials.GetClientIdAsync("twitch", Arg.Any<CancellationToken>()).Returns(clientId);
        credentials
            .GetAsync("twitch", Arg.Any<CancellationToken>())
            .Returns(
                clientId is not null && secret is not null
                    ? new SystemAppCredentials(clientId, secret)
                    : null
            );
        credentials
            .GetAsync(Arg.Is<string>(p => p != "twitch"), Arg.Any<CancellationToken>())
            .Returns((SystemAppCredentials?)null);
        credentials
            .IsAppDecisionRecordedAsync("twitch", Arg.Any<CancellationToken>())
            .Returns(decisionRecorded);

        authService
            .GetBotStatusAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new BotStatusDto(botConnected, "nomnomzbot", "NomNomzBot", null))
            );

        SystemController controller = new(
            authService,
            db,
            config,
            protector,
            credentials,
            env,
            oauthState
        );
        return (controller, credentials);
    }
}
