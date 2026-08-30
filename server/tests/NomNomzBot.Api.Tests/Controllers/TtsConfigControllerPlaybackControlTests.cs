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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the S052-queue-controls slice: <c>POST /channels/{id}/tts/playback/{skip|clear|pause|resume}</c>
/// resolves the channel's system <c>tts_caption</c> widget (get-or-create, same path <c>GET tts/overlay</c>
/// uses) and pushes a <c>tts_queue_control</c> widget event carrying the matching <c>action</c> to THAT
/// widget's id — the overlay SDK owns the live playback queue client-side, so this is the one thing the
/// server can prove: the right command, for the right widget, reached <see cref="IWidgetEventNotifier"/>.
/// </summary>
public sealed class TtsConfigControllerPlaybackControlTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid WidgetId = Guid.CreateVersion7();

    private static WidgetDetail FakeWidget() =>
        new(
            WidgetId,
            "TTS Caption",
            null,
            "vue",
            "first_party",
            true,
            "https://example.test/overlay/tts",
            null,
            null,
            new Dictionary<string, object?>(),
            ["tts_speak"],
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

    private static (
        TtsConfigController Controller,
        IWidgetService WidgetService,
        IWidgetEventNotifier Notifier
    ) Build()
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .EnsureSystemWidgetAsync(
                Broadcaster.ToString(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(FakeWidget()));
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();

        TtsConfigController controller = new(
            Substitute.For<ITtsConfigService>(),
            Substitute.For<ITtsLexiconService>(),
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ICurrentUserService>(),
            widgetService,
            Substitute.For<ITtsDispatchService>(),
            notifier
        );
        return (controller, widgetService, notifier);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("clear")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task Each_playback_action_pushes_its_own_command_to_the_resolved_widget(
        string action
    )
    {
        (TtsConfigController controller, _, IWidgetEventNotifier notifier) = Build();

        IActionResult result = action switch
        {
            "skip" => await controller.SkipPlayback(Broadcaster.ToString(), CancellationToken.None),
            "clear" => await controller.ClearPlayback(
                Broadcaster.ToString(),
                CancellationToken.None
            ),
            "pause" => await controller.PausePlayback(
                Broadcaster.ToString(),
                CancellationToken.None
            ),
            _ => await controller.ResumePlayback(Broadcaster.ToString(), CancellationToken.None),
        };

        result.Should().BeOfType<OkObjectResult>();
        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                WidgetId,
                "tts_queue_control",
                Arg.Is<object>(data =>
                    (string)data.GetType().GetProperty("action")!.GetValue(data)! == action
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_invalid_channel_id_is_rejected_before_touching_the_widget_or_notifier()
    {
        (
            TtsConfigController controller,
            IWidgetService widgetService,
            IWidgetEventNotifier notifier
        ) = Build();

        IActionResult result = await controller.SkipPlayback("not-a-guid", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await widgetService
            .DidNotReceive()
            .EnsureSystemWidgetAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_resolution_failure_short_circuits_without_pushing_a_command()
    {
        (
            TtsConfigController controller,
            IWidgetService widgetService,
            IWidgetEventNotifier notifier
        ) = Build();
        widgetService
            .EnsureSystemWidgetAsync(
                Broadcaster.ToString(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            )
            .Returns(Errors.NotFound<WidgetDetail>("Channel", Broadcaster.ToString()));

        IActionResult result = await controller.ClearPlayback(
            Broadcaster.ToString(),
            CancellationToken.None
        );

        result.Should().NotBeOfType<OkObjectResult>();
        await notifier
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
