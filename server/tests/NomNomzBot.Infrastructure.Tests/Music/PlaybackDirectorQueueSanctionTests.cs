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
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Platform.Security;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-10-19: in iFiLLz's channel the next request's start (driven by the engine's track-end report,
/// no user behind it) was refused by the outbound sanction guard, and the request was dropped.
/// </summary>
public sealed class PlaybackDirectorQueueSanctionTests
{
    [Fact]
    public async Task SpotifyPlaying_LinesUpTheNextRequest_UnderTheChannelSongRequestSanction()
    {
        OutboundSanctionAccessor sanctions = new();
        FakeEngine spotify = new(EngineKind.Spotify);
        FakeEngine youTube = new(EngineKind.YouTube);
        FairPlaybackQueue queue = new();
        using PlaybackDirector director = new(
            Channel,
            [spotify, youTube],
            queue,
            NewClock(),
            new RecordingBus(),
            sanctions
        );
        PlaybackItem first = Item("A", EngineKind.Spotify, "user1");
        PlaybackItem second = Item("B", EngineKind.Spotify, "user2");
        queue.Enqueue(first);
        queue.Enqueue(second);
        await director.HandleAsync(new RequestAdded());
        List<OutboundSanction?> seen = [];
        spotify.DuringStart = _ =>
        {
            seen.Add(sanctions.Current);
            return Task.CompletedTask;
        };

        // The engine's own report (no user behind it) confirms A, and the director lines B up.
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, first.Uri, true, 1_000, first.DurationMs)
        );

        spotify
            .Calls.Should()
            .Equal(new EngineCall("Start", "A"), new EngineCall("QueueNext", "B"));
        seen.Should().ContainSingle();
        seen[0].Should().NotBeNull("an unsanctioned start is refused by the outbound guard");
        seen[0]!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        seen[0]!.Detail.Should().Be("music:song_request_queue");
        sanctions.Current.Should().BeNull("the claim ends with the input");
    }

    [Fact]
    public async Task AUserActionInForce_StaysTheRecordedBasis()
    {
        OutboundSanctionAccessor sanctions = new();
        FakeEngine spotify = new(EngineKind.Spotify);
        FairPlaybackQueue queue = new();
        using PlaybackDirector director = new(
            Channel,
            [spotify, new FakeEngine(EngineKind.YouTube)],
            queue,
            NewClock(),
            new RecordingBus(),
            sanctions
        );
        OutboundSanction? seen = null;
        spotify.DuringStart = _ =>
        {
            seen = sanctions.Current;
            return Task.CompletedTask;
        };
        queue.Enqueue(Item("A", EngineKind.Spotify, "user1"));

        using (sanctions.Begin(OutboundSanction.UserAction("music:play", Guid.Empty)))
            await director.HandleAsync(new RequestAdded());

        seen!.Basis.Should().Be(OutboundSanctionBasis.UserAction);
    }
}
