// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Infrastructure.Sound.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A <c>play_sound</c> step's <c>handle</c> names the playing sound so a later <c>stop_sound</c> can stop just
/// that one. The handle has to survive the whole route: step, playback dto, overlay adapter, overlay payload.
/// </summary>
public sealed class PlaySoundHandleRoutingTests
{
    private static readonly Guid Channel = Guid.Parse("019f2b00-1111-7000-8000-000000000001");

    [Fact]
    public async Task The_handle_of_a_play_sound_step_reaches_the_overlay_payload()
    {
        ISoundClipService clips = Substitute.For<ISoundClipService>();
        clips
            .ResolveForPlaybackAsync(
                Channel,
                "airhorn",
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new SoundPlaybackDto(Guid.NewGuid(), "/clip.mp3", 80, 0)));
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        PlaySoundAction action = new(clips, new SoundClipOverlayNotifierAdapter(widgets));

        ActionResult result = await action.ExecuteAsync(
            new()
            {
                BroadcasterId = Channel,
                TriggeredByUserId = "viewer-9",
                TriggeredByDisplayName = "viewer",
                MessageId = "m1",
                RawMessage = "!airhorn",
                CancellationToken = default,
            },
            new()
            {
                Type = "play_sound",
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["clip"] = JsonSerializer.SerializeToElement("airhorn"),
                    ["handle"] = JsonSerializer.SerializeToElement("x"),
                },
            }
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        PlaySoundPayload sent = (PlaySoundPayload)
            widgets.ReceivedCalls().Single().GetArguments()[1]!;
        sent.Handle.Should().Be("x");
        sent.PlaybackUrl.Should().Be("/clip.mp3");
        sent.Volume.Should().Be(80);
    }
}
