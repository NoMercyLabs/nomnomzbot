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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>POST /channels/{id}/tts/playback/{skip|clear|pause|resume}</c> hands the matching command to the
/// channel's one audio page (<see cref="IWidgetNotifier.TtsQueueControlAsync"/>) — the overlay SDK owns the
/// live playback queue client-side, so the server's part is routing the right command to the right channel.
/// </summary>
public sealed class TtsConfigControllerPlaybackControlTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static (TtsConfigController Controller, IWidgetNotifier Notifier) Build()
    {
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        TtsConfigController controller = new(
            Substitute.For<ITtsConfigService>(),
            Substitute.For<ITtsLexiconService>(),
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<ITtsDispatchService>(),
            notifier
        );
        return (controller, notifier);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("clear")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task Each_playback_action_pushes_its_own_command_to_the_audio_page(string action)
    {
        (TtsConfigController controller, IWidgetNotifier notifier) = Build();

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
            .TtsQueueControlAsync(Broadcaster.ToString(), action, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_channel_id_is_rejected_before_touching_the_notifier()
    {
        (TtsConfigController controller, IWidgetNotifier notifier) = Build();

        IActionResult result = await controller.SkipPlayback("not-a-guid", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await notifier
            .DidNotReceive()
            .TtsQueueControlAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
