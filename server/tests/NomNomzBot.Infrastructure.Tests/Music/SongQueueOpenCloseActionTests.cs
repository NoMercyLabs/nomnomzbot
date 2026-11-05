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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <c>song_queue_open</c> / <c>song_queue_close</c> call <see cref="ISongRequestIntakeService.SetRequestsOpenAsync"/>
/// with the right open flag for THIS channel, and that the service's failure propagates rather than being
/// swallowed as a success.
/// </summary>
public sealed class SongQueueOpenCloseActionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac101");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-1",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };

    [Fact]
    public async Task Song_queue_open_opens_the_queue_for_this_channel()
    {
        ISongRequestIntakeService intake = Substitute.For<ISongRequestIntakeService>();
        intake
            .SetRequestsOpenAsync(ChannelId.ToString(), true, Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        SongQueueOpenAction action = new(intake);

        ActionResult result = await action.ExecuteAsync(Ctx(), new() { Type = "song_queue_open" });

        result.Succeeded.Should().BeTrue();
        await intake
            .Received(1)
            .SetRequestsOpenAsync(ChannelId.ToString(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Song_queue_close_closes_the_queue_for_this_channel()
    {
        ISongRequestIntakeService intake = Substitute.For<ISongRequestIntakeService>();
        intake
            .SetRequestsOpenAsync(ChannelId.ToString(), false, Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        SongQueueCloseAction action = new(intake);

        ActionResult result = await action.ExecuteAsync(Ctx(), new() { Type = "song_queue_close" });

        result.Succeeded.Should().BeTrue();
        await intake
            .Received(1)
            .SetRequestsOpenAsync(ChannelId.ToString(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Song_queue_open_propagates_a_service_failure()
    {
        ISongRequestIntakeService intake = Substitute.For<ISongRequestIntakeService>();
        intake
            .SetRequestsOpenAsync(ChannelId.ToString(), true, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<bool>("channel not found", "NOT_FOUND"));
        SongQueueOpenAction action = new(intake);

        ActionResult result = await action.ExecuteAsync(Ctx(), new() { Type = "song_queue_open" });

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("channel not found");
    }
}
