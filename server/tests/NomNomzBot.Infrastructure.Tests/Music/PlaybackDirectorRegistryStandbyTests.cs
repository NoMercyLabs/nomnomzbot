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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Blue/green overlap: one instance drives a channel's playback. A restored queue waits in both instances, and
/// only the active one may start its head.
/// </summary>
public sealed class PlaybackDirectorRegistryStandbyTests
{
    private static readonly Guid Channel = PlaybackDirectorTestKit.Channel;

    private static (
        PlaybackDirectorRegistry Registry,
        PlaybackDirectorTestKit.FakeEngineFactory Engines
    ) Build(bool active)
    {
        SongRequestQueueStore store = new();
        store
            .GetOrCreate(Channel.ToString())
            .Enqueue(
                "viewer",
                new SongRequestEntry(
                    "spotify:track:a",
                    "Track a",
                    "Artist",
                    null,
                    200000,
                    "viewer",
                    0,
                    null,
                    ""
                )
            );
        PlaybackDirectorTestKit.FakeEngineFactory engines = new();
        IActiveInstanceGate gate = Substitute.For<IActiveInstanceGate>();
        gate.IsActiveInstance.Returns(active);

        return (
            new(
                store,
                engines,
                new PlaybackDirectorTestKit.RecordingBus(),
                TimeProvider.System,
                null,
                gate
            ),
            engines
        );
    }

    [Fact]
    public async Task A_standby_instance_does_not_start_the_restored_head()
    {
        (PlaybackDirectorRegistry registry, PlaybackDirectorTestKit.FakeEngineFactory engines) =
            Build(active: false);

        await registry.SendAsync(Channel, new RequestAdded());
        await registry.SendAsync(Channel, new OutputAttached());

        registry.StateOf(Channel).Kind.Should().Be(PlaybackStateKind.Idle);
        engines.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task The_active_instance_starts_the_restored_head_once()
    {
        (PlaybackDirectorRegistry registry, PlaybackDirectorTestKit.FakeEngineFactory engines) =
            Build(active: true);

        await registry.SendAsync(Channel, new RequestAdded());

        engines
            .EngineOf(Channel, EngineKind.Spotify)
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .ContainSingle();
    }
}
