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
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Twitch;

/// <summary>
/// Twitch answers Get Channel Stream Schedule with 404 when a channel has never saved a schedule. Live
/// 2026-10-19 the Stream page in iFiLLz's channel showed "The schedule could not load." for that.
/// </summary>
public sealed class TwitchScheduleApiNotFoundTests
{
    private static readonly Guid Channel = Guid.Parse("01a149e1-4d8b-7000-97d7-03a61380a2b2");

    [Fact]
    public async Task A404_FromTwitch_IsAnEmptySchedule()
    {
        ITwitchHelixTransport transport = Substitute.For<ITwitchHelixTransport>();
        transport
            .GetSingleAsync<TwitchSchedule>(
                Arg.Any<TwitchHelixRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchSchedule>("Not Found", TwitchErrorCodes.NotFound));
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity.GetTwitchChannelIdAsync(Channel, Arg.Any<CancellationToken>()).Returns("12345");
        TwitchScheduleApi sut = new(transport, identity, Substitute.For<ITwitchTokenResolver>());

        Result<TwitchSchedule> result = await sut.GetScheduleAsync(
            Channel,
            new TwitchPageRequest()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Segments.Should().BeEmpty();
        result.Value.BroadcasterId.Should().Be("12345");
        result.Value.Vacation.Should().BeNull();
    }

    [Fact]
    public async Task AnyOtherFailure_StaysAFailure()
    {
        ITwitchHelixTransport transport = Substitute.For<ITwitchHelixTransport>();
        transport
            .GetSingleAsync<TwitchSchedule>(
                Arg.Any<TwitchHelixRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchSchedule>("Unauthorized", TwitchErrorCodes.Unauthorized));
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity.GetTwitchChannelIdAsync(Channel, Arg.Any<CancellationToken>()).Returns("12345");
        TwitchScheduleApi sut = new(transport, identity, Substitute.For<ITwitchTokenResolver>());

        Result<TwitchSchedule> result = await sut.GetScheduleAsync(
            Channel,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
    }
}
