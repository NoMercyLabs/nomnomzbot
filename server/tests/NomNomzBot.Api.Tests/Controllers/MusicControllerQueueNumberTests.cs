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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the dashboard's queue read carries, for every entry, the same number <c>!queue</c> prints and
/// <c>!wrongsong N</c> reads back (<see cref="QueueNumbering"/>), so the list never shows a number a viewer
/// cannot type.
/// </summary>
public sealed class MusicControllerQueueNumberTests
{
    private const string ChannelId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task GetQueue_numbers_each_entry_exactly_as_the_queue_command_does()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [
                        new("First", "A", null, 1000, "viewer1"),
                        new("Second", "B", null, 1000, "viewer2"),
                        new("Third", "C", null, 1000, "viewer3"),
                    ]
                )
            );
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<MusicConfigDto>("none", "NOT_FOUND"));
        IMusicHistoryReader history = Substitute.For<IMusicHistoryReader>();
        history
            .ResolveRequestersAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, RequesterIdentityDto>());
        MusicController controller = new(
            music,
            config,
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            history,
            Substitute.For<ISongRequestIntakeService>()
        );

        IActionResult result = await controller.GetQueue(ChannelId, CancellationToken.None);

        MusicQueueDto dto = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<MusicQueueDto>>()
            .Which.Data!;
        dto.Queue.Select(i => i.Number)
            .Should()
            .Equal([
                QueueNumbering.NumberOf(0),
                QueueNumbering.NumberOf(1),
                QueueNumbering.NumberOf(2),
            ]);
        dto.Queue.Select(i => i.Number).Should().Equal(1, 2, 3);
        dto.Queue.Select(i => i.Position).Should().Equal(0, 1, 2);
    }
}
