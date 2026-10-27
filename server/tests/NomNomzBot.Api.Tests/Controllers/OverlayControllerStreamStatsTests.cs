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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay <c>stream-stats</c> read is what the widget SDK's <c>data.streamStats</c> calls: the token picks the
/// channel, the <c>metrics</c> list picks the keys, and the answer is the dashboard's own stream-stats answer.
/// </summary>
public sealed class OverlayControllerStreamStatsTests
{
    private static readonly Guid Channel = Guid.Parse("0192f000-0000-7000-8000-0000000000a1");

    private static (
        OverlayController Controller,
        IStreamStatsService Stats,
        IWidgetService Widgets
    ) Create()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .ResolveBroadcasterIdByOverlayTokenAsync("good-token", Arg.Any<CancellationToken>())
            .Returns(Channel);
        IStreamStatsService stats = Substitute.For<IStreamStatsService>();
        OverlayController controller = new(widgets, stats, Substitute.For<ITemplateResolver>())
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return (controller, stats, widgets);
    }

    [Fact]
    public async Task Only_the_requested_keys_reach_the_read_and_only_their_values_come_back()
    {
        (OverlayController controller, IStreamStatsService stats, _) = Create();
        stats
            .GetAsync(
                Channel,
                "s1",
                Arg.Is<IReadOnlyCollection<string>>(k =>
                    k.SequenceEqual(new[] { "duration", "bits" })
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new StreamStatsDto
                    {
                        Stream = new() { Id = "s1" },
                        Duration = 3600,
                    }
                )
            );

        IActionResult result = await controller.GetStreamStats(
            "s1",
            "duration, bits,",
            "good-token",
            CancellationToken.None
        );

        StreamStatsDto data = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<StatusResponseDto<StreamStatsDto>>()
            .Subject.Data!;
        data.Stream.Id.Should().Be("s1");
        data.Duration.Should().Be(3600);
        data.Followers.Should().BeNull("a key nobody asked for stays out of the answer");
    }

    [Fact]
    public async Task An_unknown_metric_key_is_a_400_that_names_the_key()
    {
        (OverlayController controller, IStreamStatsService stats, _) = Create();
        stats
            .GetAsync(
                Channel,
                null,
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<StreamStatsDto>(
                    "Unknown stream metric 'nope'. Known metrics: bits, chat.",
                    "VALIDATION_FAILED"
                )
            );

        IActionResult result = await controller.GetStreamStats(
            null,
            "nope",
            "good-token",
            CancellationToken.None
        );

        StatusResponseDto<object> body = result
            .Should()
            .BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Message.Should().Contain("'nope'");
    }

    [Fact]
    public async Task An_unknown_overlay_token_is_a_404_and_never_reaches_the_stats_read()
    {
        (OverlayController controller, IStreamStatsService stats, _) = Create();

        IActionResult result = await controller.GetStreamStats(
            null,
            "duration",
            "bad-token",
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundObjectResult>();
        await stats
            .DidNotReceiveWithAnyArgs()
            .GetAsync(default, null, null, CancellationToken.None);
    }
}
