// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Auth;

/// <summary>
/// REQUIREMENT (identity-auth §3.1, Bot Account device flow): the bot account connects via the SAME no-secret
/// Device Code Flow as the streamer — a start that mints a <see cref="DeviceCodeStartDto"/> the operator approves
/// at twitch.tv/activate, then a poll that connects + vaults the shared-bot tokens. So <see cref="IAuthService"/>
/// must expose a bot device-login seam that mirrors the user device-login seam, and be wired in the real
/// container. These tests DEMAND that seam; a red means the bot cannot sign in the way the spec requires.
/// </summary>
public sealed class BotDeviceLoginSeamTests : IClassFixture<DiHostFixture>
{
    private readonly DiHostFixture _host;

    public BotDeviceLoginSeamTests(DiHostFixture host) => _host = host;

    [Fact]
    public void Auth_service_is_registered_in_the_container()
    {
        IServiceProviderIsService inspector =
            _host.Services.GetRequiredService<IServiceProviderIsService>();

        inspector
            .IsService(typeof(IAuthService))
            .Should()
            .BeTrue(
                "IAuthService owns Twitch identity/login + the bot device flow and must be wired"
            );
    }

    [Fact]
    public void Bot_device_login_seam_mirrors_the_user_device_login_seam()
    {
        Type contract = typeof(IAuthService);

        MethodInfo? startBot = contract.GetMethod(nameof(IAuthService.StartBotDeviceLoginAsync));
        MethodInfo? pollBot = contract.GetMethod(nameof(IAuthService.PollBotDeviceLoginAsync));
        MethodInfo? startUser = contract.GetMethod(
            nameof(IAuthService.StartTwitchDeviceLoginAsync)
        );
        MethodInfo? pollUser = contract.GetMethod(nameof(IAuthService.PollTwitchDeviceLoginAsync));

        startBot
            .Should()
            .NotBeNull(
                "the bot account must start a device login (twitch.tv/activate) like the streamer"
            );
        pollBot
            .Should()
            .NotBeNull("the bot device login must be pollable to connect + vault the bot tokens");
        startUser
            .Should()
            .NotBeNull("the streamer device-login start is the seam the bot flow mirrors");
        pollUser
            .Should()
            .NotBeNull("the streamer device-login poll is the seam the bot flow mirrors");

        // Same start shape as the user flow → it is genuinely the SAME device flow, not a divergent handshake.
        startBot!
            .ReturnType.Should()
            .Be(
                typeof(Task<Result<DeviceCodeStartDto>>),
                "the bot device start must return the same DeviceCodeStartDto as the streamer start"
            );
        startUser!
            .ReturnType.Should()
            .Be(
                typeof(Task<Result<DeviceCodeStartDto>>),
                "the streamer start returns DeviceCodeStartDto"
            );

        // Bot poll connects the bot account (DeviceBotPollDto carries the BotStatus), distinct from the streamer
        // poll (DeviceLoginPollDto carries the session AuthResult) — parallel seams, one shared handshake.
        pollBot!
            .ReturnType.Should()
            .Be(
                typeof(Task<Result<DeviceBotPollDto>>),
                "the bot poll resolves to a bot connection (DeviceBotPollDto), not a user session"
            );

        startBot
            .Should()
            .NotBeSameAs(
                startUser,
                "bot login must be a first-class parallel seam, not an alias of the streamer login"
            );
    }
}
