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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-10-11: <c>!wrongsong</c> dropped the request from the fair queue but the song stayed in the
/// streamer's Spotify queue and played. Spotify has no remove-from-queue call, so the only honest undo is
/// to read Spotify's LIVE queue and skip the wrong track when it starts — whether or not our in-flight
/// pointer still names it — while every song the streamer queued by hand is left exactly as it is.
/// </summary>
public sealed class SongWrongSpotifyQueueTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab05");
    private const string WrongUri = "spotify:track:wrong";
    private const string HandQueuedUri = "spotify:track:handqueued";

    private sealed class Harness
    {
        public required MusicService Sut { get; init; }
        public required SongRequestQueueStore Store { get; init; }
        public required List<string> Pushed { get; init; }

        /// <summary>What Spotify's live queue answers; null = "cannot tell".</summary>
        public IReadOnlyList<TrackInfo>? SpotifyQueue { get; set; }

        public string Channel => ChannelId.ToString();
    }

    private static Harness Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        SongRequestQueueStore store = new();
        List<string> pushed = [];
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        MusicService sut = new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>()
        );

        Harness harness = new()
        {
            Sut = sut,
            Store = store,
            Pushed = pushed,
        };
        provider.Provider.Returns("spotify");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                pushed.Add(call.ArgAt<string>(1));
                return Task.FromResult(true);
            });
        provider
            .GetQueueAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(harness.SpotifyQueue));

        // The wrong request sits in our fair queue. The in-flight pointer is deliberately NOT set: this is
        // the state in which the old code never armed a skip although Spotify still held the song.
        store
            .GetOrCreate(ChannelId.ToString())
            .Enqueue(
                "pseudo109",
                new(WrongUri, "Wrong Song", "Artist", null, 200_000, "pseudo109", 0, null, "K7QM")
            );
        return harness;
    }

    private static TrackInfo Track(string uri) =>
        new()
        {
            TrackName = uri,
            Artist = "Artist",
            Album = "Album",
            TrackUri = uri,
            Provider = "spotify",
        };

    private static PlaybackStateChangedEvent PlaybackOf(string trackUri) =>
        new()
        {
            BroadcasterId = ChannelId,
            IsPlaying = true,
            TrackUri = trackUri,
            TrackName = trackUri,
            Provider = "spotify",
            ObservedAt = DateTimeOffset.UtcNow,
        };

    private static (SongRetractionSkipHandler Handler, IMusicService Music) BuildHandler(
        SongRequestQueueStore store
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                ChannelId.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return (new(store, music, NullLogger<SongRetractionSkipHandler>.Instance), music);
    }

    private static int SkipCalls(IMusicService music) =>
        music.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMusicService.SkipAsync));

    [Fact]
    public async Task A_request_Spotify_still_holds_is_skipped_when_it_starts_and_hand_queued_songs_are_untouched()
    {
        Harness h = Build();
        h.SpotifyQueue = [Track(WrongUri), Track(HandQueuedUri)];
        (SongRetractionSkipHandler handler, IMusicService music) = BuildHandler(h.Store);

        RequestRetraction result = await h.Sut.RetractRequestAsync(h.Channel, 0);

        result.Should().Be(RequestRetraction.RemovedWillSkip);
        (await h.Sut.GetQueueAsync(h.Channel)).Queue.Should().BeEmpty();

        await handler.HandleAsync(PlaybackOf(WrongUri));
        SkipCalls(music).Should().Be(1, "the wrong song is skipped the moment it starts");

        await handler.HandleAsync(PlaybackOf(HandQueuedUri));
        SkipCalls(music).Should().Be(1, "the song the streamer queued by hand still plays");
        h.Pushed.Should().BeEmpty("nothing is re-added to Spotify's queue");
    }

    [Fact]
    public async Task A_request_Spotify_no_longer_holds_arms_nothing_and_says_removed()
    {
        Harness h = Build();
        h.SpotifyQueue = [Track(HandQueuedUri)];
        (SongRetractionSkipHandler handler, IMusicService music) = BuildHandler(h.Store);

        RequestRetraction result = await h.Sut.RetractRequestAsync(h.Channel, 0);

        result.Should().Be(RequestRetraction.Removed);
        await handler.HandleAsync(PlaybackOf(WrongUri));
        SkipCalls(music)
            .Should()
            .Be(0, "a later legitimate play of the same song must not be skipped");
    }

    [Fact]
    public async Task An_unreadable_Spotify_queue_does_not_block_the_removal()
    {
        Harness h = Build();
        h.SpotifyQueue = null;

        RequestRetraction result = await h.Sut.RetractRequestAsync(h.Channel, 0);

        result.Should().Be(RequestRetraction.Removed);
        (await h.Sut.GetQueueAsync(h.Channel)).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task The_chat_reply_says_will_skip_when_Spotify_still_holds_the_song()
    {
        Harness h = Build();
        h.SpotifyQueue = [Track(WrongUri)];
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendReplyAsync(
                ChannelId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        SongWrongAction action = new(
            h.Sut,
            MusicTestDbContext.New(),
            chat,
            NullLogger<SongWrongAction>.Instance
        );
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-109",
            TriggeredByDisplayName = "pseudo109",
            MessageId = "msg-1",
            RawMessage = "!wrongsong",
        };

        ActionResult result = await action.ExecuteAsync(
            ctx,
            new() { Type = "song_wrong", Parameters = new() }
        );

        result.Output.Should().Be("removed: Wrong Song");
        await chat.Received(1)
            .SendReplyAsync(
                ChannelId,
                "msg-1",
                Arg.Is<string>(t => t.Contains("Will skip Wrong Song by Artist when it starts")),
                Arg.Any<CancellationToken>()
            );
    }
}
