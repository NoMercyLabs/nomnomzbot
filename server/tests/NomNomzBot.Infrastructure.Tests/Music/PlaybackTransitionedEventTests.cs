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
using Newtonsoft.Json;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class PlaybackTransitionedEventTests
{
    private static PlaybackTransitionedEvent Sample() =>
        new()
        {
            BroadcasterId = PlaybackDirectorTestKit.Channel,
            From = "Playing",
            To = "Starting",
            Input = "OwnerSkip",
            Engine = "YouTube",
            QueueItemId = "row-7",
            Reason = "stop_unconfirmed",
        };

    [Fact]
    public void Journal_TypeNameIsTheClassName_AndRegistryResolvesIt()
    {
        string journalType = typeof(PlaybackTransitionedEvent).Name;

        journalType.Should().Be("PlaybackTransitionedEvent");
        new DomainEventTypeRegistry()
            .Resolve(journalType)
            .Should()
            .Be(typeof(PlaybackTransitionedEvent));
    }

    [Fact]
    public void Payload_RoundTripsThroughTheJournalSerializerAndRegistry_WithEveryField()
    {
        PlaybackTransitionedEvent original = Sample();
        string payload = JsonConvert.SerializeObject(original);

        Type type = new DomainEventTypeRegistry().Resolve("PlaybackTransitionedEvent")!;
        PlaybackTransitionedEvent restored = (PlaybackTransitionedEvent)
            JsonConvert.DeserializeObject(payload, type)!;

        restored.From.Should().Be("Playing");
        restored.To.Should().Be("Starting");
        restored.Input.Should().Be("OwnerSkip");
        restored.Engine.Should().Be("YouTube");
        restored.QueueItemId.Should().Be("row-7");
        restored.Reason.Should().Be("stop_unconfirmed");
        restored.BroadcasterId.Should().Be(PlaybackDirectorTestKit.Channel);
        restored.EventId.Should().Be(original.EventId);
    }
}
