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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.MediaShare.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>Proves the media_playback pipeline action hands the right command to the player service.</summary>
public sealed class MediaPlaybackActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000c001");

    private static (MediaPlaybackAction Action, IMediaSharePlaybackService Playback) Build()
    {
        IMediaSharePlaybackService playback = Substitute.For<IMediaSharePlaybackService>();
        playback
            .SendCommandAsync(
                Arg.Any<Guid>(),
                Arg.Any<MediaPlaybackCommandRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(MediaPlayerStateDto.Idle));
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.ArgAt<string>(0));
        return (new(playback, templates), playback);
    }

    private static Task<ActionResult> Run(
        MediaPlaybackAction action,
        string command,
        string? value = null
    )
    {
        Dictionary<string, JsonElement> parameters = new()
        {
            ["command"] = JsonSerializer.SerializeToElement(command),
        };
        if (value is not null)
            parameters["value"] = JsonSerializer.SerializeToElement(value);

        return action.ExecuteAsync(
            new PipelineExecutionContext
            {
                BroadcasterId = Channel,
                TriggeredByUserId = "1",
                TriggeredByDisplayName = "mod",
                MessageId = "m1",
                RawMessage = "!media",
            },
            new ActionDefinition { Type = "media_playback", Parameters = parameters }
        );
    }

    [Fact]
    public async Task Volume_SendsTheNumberToThePlayer()
    {
        (MediaPlaybackAction action, IMediaSharePlaybackService playback) = Build();

        ActionResult result = await Run(action, "volume", "40");

        result.Succeeded.Should().BeTrue();
        await playback
            .Received(1)
            .SendCommandAsync(
                Channel,
                Arg.Is<MediaPlaybackCommandRequest>(r => r.Command == "volume" && r.Volume == 40),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PlayNow_SendsTheRequestId()
    {
        (MediaPlaybackAction action, IMediaSharePlaybackService playback) = Build();
        Guid id = Guid.NewGuid();

        ActionResult result = await Run(action, "play_now", id.ToString());

        result.Succeeded.Should().BeTrue();
        await playback
            .Received(1)
            .SendCommandAsync(
                Channel,
                Arg.Is<MediaPlaybackCommandRequest>(r =>
                    r.Command == "play_now" && r.RequestId == id
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PlayNow_TakesTheUlidTheDashboardSends()
    {
        (MediaPlaybackAction action, IMediaSharePlaybackService playback) = Build();
        Guid id = Guid.NewGuid();

        ActionResult result = await Run(action, "play_now", OwnedIdCodec.Encode(id));

        result.Succeeded.Should().BeTrue();
        await playback
            .Received(1)
            .SendCommandAsync(
                Channel,
                Arg.Is<MediaPlaybackCommandRequest>(r =>
                    r.Command == "play_now" && r.RequestId == id
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Seek_WithATextValue_FailsWithoutCallingThePlayer()
    {
        (MediaPlaybackAction action, IMediaSharePlaybackService playback) = Build();

        ActionResult result = await Run(action, "seek", "soon");

        result.Succeeded.Should().BeFalse();
        await playback.DidNotReceiveWithAnyArgs().SendCommandAsync(default, default!);
    }

    [Fact]
    public void Command_IsAnEnumFieldListingEveryPlaybackCommand()
    {
        (MediaPlaybackAction action, _) = Build();

        PipelineActionFieldDescriptor field = action.Fields.Single(f => f.Name == "command");

        field.Kind.Should().Be(PipelineActionFieldKind.Enum);
        field.Options.Should().BeEquivalentTo(MediaPlaybackCommands.All);
    }
}
