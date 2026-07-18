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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the Discord guild reads no longer leak a raw HTTP 500 when Discord rejects the bot token: an upstream
/// 401/403 surfaces from the gateway as <c>DISCORD_UNAUTHORIZED</c>, which the controller maps to a 409 (a
/// reconnect-the-bot state) carrying the reconnect message — so the client shows a reconnect prompt instead of a
/// generic failure. A missing connection maps the same way; a genuine upstream outage maps to 503, never 500.
/// </summary>
public sealed class DiscordGuildErrorMappingTests
{
    private static readonly Guid Channel = Guid.CreateVersion7();
    private static readonly Guid Connection = Guid.CreateVersion7();

    private static (DiscordController Controller, IDiscordGuildDirectoryService Directory) Build()
    {
        IDiscordGuildDirectoryService directory = Substitute.For<IDiscordGuildDirectoryService>();
        DiscordController controller = new(
            Substitute.For<IDiscordGuildService>(),
            Substitute.For<IDiscordNotificationConfigService>(),
            Substitute.For<IDiscordNotificationRoleService>(),
            Substitute.For<IDiscordNotificationDispatcher>(),
            directory
        );
        return (controller, directory);
    }

    [Fact]
    public async Task Guild_roles_read_maps_a_discord_401_to_a_409_reconnect_signal_not_a_500()
    {
        (DiscordController controller, IDiscordGuildDirectoryService directory) = Build();
        const string reconnect =
            "Discord authorization is invalid or expired. Reconnect the Discord bot to continue.";
        directory
            .GetGuildRolesAsync(Channel, Connection, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<DiscordGuildRoleDto>>(
                    reconnect,
                    "DISCORD_UNAUTHORIZED"
                )
            );

        IActionResult result = await controller.GetGuildRoles(
            Channel,
            Connection,
            CancellationToken.None
        );

        ObjectResult obj = result.Should().BeAssignableTo<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        obj.StatusCode.Should().NotBe(StatusCodes.Status500InternalServerError);
        StatusResponseDto<object> body = obj
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Message.Should().Be(reconnect);
    }

    [Fact]
    public async Task Guild_channels_read_maps_a_discord_upstream_error_to_503_not_500()
    {
        (DiscordController controller, IDiscordGuildDirectoryService directory) = Build();
        directory
            .GetGuildChannelsAsync(Channel, Connection, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<DiscordGuildChannelDto>>(
                    "Discord request failed (500).",
                    "DISCORD_ERROR"
                )
            );

        IActionResult result = await controller.GetGuildChannels(
            Channel,
            Connection,
            CancellationToken.None
        );

        ObjectResult obj = result.Should().BeAssignableTo<ObjectResult>().Subject;
        // An upstream Discord failure is never our 500 — it maps to a 503 (upstream unavailable).
        obj.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }
}
