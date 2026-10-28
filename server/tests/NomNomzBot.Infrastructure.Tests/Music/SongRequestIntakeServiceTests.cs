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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Closing the queue stores <c>RequestsOpen = false</c> and pushes a queue-changed event, so the dashboard and
/// the viewer pages update live. Asking for the state it already has stores and pushes nothing.
/// </summary>
public sealed class SongRequestIntakeServiceTests
{
    private const string Channel = "0192a000-0000-7000-8000-0000000ac0c2";

    private static IMusicConfigService Config(bool requestsOpen)
    {
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new MusicConfigDto(
                        true,
                        "auto",
                        50,
                        5,
                        true,
                        true,
                        "Everyone",
                        RequestsOpen: requestsOpen
                    )
                )
            );
        config
            .UpdateConfigAsync(
                Channel,
                Arg.Any<UpdateMusicConfigDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(
                    new MusicConfigDto(
                        true,
                        "auto",
                        50,
                        5,
                        true,
                        true,
                        "Everyone",
                        RequestsOpen: call.Arg<UpdateMusicConfigDto>().RequestsOpen!.Value
                    )
                )
            );
        return config;
    }

    [Fact]
    public async Task Closing_an_open_queue_stores_closed_and_pushes_the_queue()
    {
        IMusicConfigService config = Config(requestsOpen: true);
        IEventBus bus = Substitute.For<IEventBus>();
        SongRequestIntakeService intake = new(config, Substitute.For<IMusicService>(), bus);

        Result<bool> result = await intake.SetRequestsOpenAsync(Channel, false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await config
            .Received(1)
            .UpdateConfigAsync(
                Channel,
                Arg.Is<UpdateMusicConfigDto>(u => u.RequestsOpen == false),
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<SongRequestQueueChangedEvent>(e => e.BroadcasterId == Guid.Parse(Channel)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Closing_a_closed_queue_stores_and_pushes_nothing()
    {
        IMusicConfigService config = Config(requestsOpen: false);
        IEventBus bus = Substitute.For<IEventBus>();
        SongRequestIntakeService intake = new(config, Substitute.For<IMusicService>(), bus);

        Result<bool> result = await intake.SetRequestsOpenAsync(Channel, false);

        result.Value.Should().BeFalse();
        await config.DidNotReceiveWithAnyArgs().UpdateConfigAsync(default!, default!, default);
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<SongRequestQueueChangedEvent>(), default);
    }
}
