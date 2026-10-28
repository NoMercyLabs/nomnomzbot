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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /channels/{id}/discord/triggers</c> hands back the trigger list the config service holds,
/// in a 200 envelope, so the dashboard reads the list from the server and not from a copy.
/// </summary>
public sealed class DiscordTriggersEndpointTests
{
    [Fact]
    public void GetTriggers_ReturnsTheServicesTriggerList_InA200Envelope()
    {
        IDiscordNotificationConfigService configs =
            Substitute.For<IDiscordNotificationConfigService>();
        IReadOnlyList<DiscordNotificationTriggerDto> triggers =
        [
            new("go_live", "dashboard.discord.triggers.go_live", ["title", "game"]),
            new("follow", "dashboard.discord.triggers.follow", ["user.name"]),
        ];
        configs.GetTriggers().Returns(Result.Success(triggers));
        DiscordController controller = new(
            Substitute.For<IDiscordGuildService>(),
            configs,
            Substitute.For<IDiscordLiveRoleConfigService>(),
            Substitute.For<IDiscordNotificationRoleService>(),
            Substitute.For<IDiscordNotificationDispatcher>(),
            Substitute.For<IDiscordGuildDirectoryService>()
        );

        IActionResult result = controller.GetTriggers(Guid.CreateVersion7());

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<DiscordNotificationTriggerDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<DiscordNotificationTriggerDto>>>()
            .Subject;
        body.Data.Should().Equal(triggers);
    }
}
