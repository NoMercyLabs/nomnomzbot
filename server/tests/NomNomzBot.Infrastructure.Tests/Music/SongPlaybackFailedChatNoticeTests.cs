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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A song request the overlay player could not play is skipped; chat says so, naming the requester, so the
/// viewer knows their song will not play and the streamer sees why the queue moved on.
/// </summary>
public sealed class SongPlaybackFailedChatNoticeTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000a7d01");
    private const string Uri = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private static SongRequestEntry Entry(string requestedBy) =>
        new(Uri, "Never Gonna", "Rick", null, 213_000, requestedBy, 0, null, "K7QM");

    private static (SongPlaybackFailedChatNotice Notice, List<string> Sent) Build(
        SongRequestQueueStore store,
        PlaybackState? state = null
    )
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        List<string> sent = [];
        chat.SendMessageAsync(Channel, Arg.Do<string>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        IPlaybackStateReader playbackState = Substitute.For<IPlaybackStateReader>();
        playbackState.StateOf(Channel).Returns(state ?? PlaybackState.Idle);
        return (new(chat, store, playbackState), sent);
    }

    private static SongPlaybackFailedEvent Failed() =>
        new()
        {
            BroadcasterId = Channel,
            TrackUri = Uri,
            TrackName = "Never Gonna",
        };

    [Fact]
    public async Task A_request_that_never_started_names_its_requester()
    {
        SongRequestQueueStore store = new();
        store.SetInFlight(Channel.ToString(), Entry("anda_six"));
        (SongPlaybackFailedChatNotice notice, List<string> sent) = Build(store);

        await notice.HandleAsync(Failed());

        sent.Should().ContainSingle();
        sent[0].Should().StartWith("@anda_six ").And.Contain("\"Never Gonna\"");
        sent[0].Should().Contain("next");
    }

    [Fact]
    public async Task A_request_that_failed_mid_song_names_its_requester()
    {
        SongRequestQueueStore store = new();
        store.SetPlayingRequest(Channel.ToString(), Entry("fillz_viewer"));
        (SongPlaybackFailedChatNotice notice, List<string> sent) = Build(store);

        await notice.HandleAsync(Failed());

        sent.Should().ContainSingle().Which.Should().StartWith("@fillz_viewer ");
    }

    [Fact]
    public async Task A_lined_up_request_the_director_started_names_its_requester()
    {
        PlaybackItem item = new(
            "K7QM",
            "anda_six",
            EngineKind.YouTube,
            Uri,
            "Never Gonna",
            ["Rick"],
            null,
            213_000,
            "anda_six"
        );
        (SongPlaybackFailedChatNotice notice, List<string> sent) = Build(
            new(),
            PlaybackState.Starting(EngineKind.YouTube, item)
        );

        await notice.HandleAsync(Failed());

        sent.Should().ContainSingle().Which.Should().StartWith("@anda_six ");
    }

    [Fact]
    public async Task A_song_nobody_requested_is_still_announced_without_a_mention()
    {
        (SongPlaybackFailedChatNotice notice, List<string> sent) = Build(new());

        await notice.HandleAsync(Failed());

        sent.Should().ContainSingle();
        sent[0].Should().StartWith("\"Never Gonna\"").And.NotContain("@");
    }
}
