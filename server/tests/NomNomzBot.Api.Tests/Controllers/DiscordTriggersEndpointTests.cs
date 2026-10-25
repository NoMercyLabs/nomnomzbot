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
