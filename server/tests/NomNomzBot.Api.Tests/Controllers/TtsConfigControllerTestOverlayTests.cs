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
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>POST /channels/{id}/tts/overlay/test</c> dispatches a REAL utterance through
/// <see cref="ITtsDispatchService"/> — the same method production reward-triggered TTS uses — rather than the
/// synthesis-only <c>TestVoice</c> shortcut. <c>ChannelEventId</c> must be explicitly <c>null</c>: this is a
/// standalone dashboard-triggered test, not a paid channel event, so nothing is created for Replay to correlate.
/// </summary>
public sealed class TtsConfigControllerTestOverlayTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static TtsConfigController Build(
        ITtsDispatchService dispatch,
        IWidgetService? widgetService = null
    ) =>
        new(
            Substitute.For<ITtsConfigService>(),
            Substitute.For<ITtsLexiconService>(),
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ICurrentUserService>(),
            widgetService ?? Substitute.For<IWidgetService>(),
            dispatch,
            Substitute.For<IWidgetEventNotifier>()
        );

    [Fact]
    public async Task TestOverlay_dispatches_a_real_utterance_with_null_channel_event_id()
    {
        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "en-US-JennyNeural",
                        "edge",
                        44,
                        1200,
                        "https://example.test/audio.mp3"
                    )
                )
            );

        TtsConfigController controller = Build(dispatch);

        IActionResult result = await controller.TestOverlay(
            Broadcaster.ToString(),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();

        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.BroadcasterId == Broadcaster
                    && r.ChannelEventId == null
                    && !string.IsNullOrWhiteSpace(r.Text)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TestOverlay_returns_bad_request_for_an_invalid_channel_id()
    {
        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        TtsConfigController controller = Build(dispatch);

        IActionResult result = await controller.TestOverlay("not-a-guid", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await dispatch
            .DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }
}
