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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-09-24: the streamer deleted a handed-over song request from the Spotify app's own queue. It
/// never played, so SongRequestQueueReconciler never cleared it, and HandOverNextAsync refused to send
/// anything else while it sat in flight — the queue froze with nothing saying why. These pin that the
/// recovery tick notices the loss, drops only that request, moves the queue on and tells chat — and that
/// it never drops a request on anything short of proof.
/// </summary>
public sealed class SongRequestLostAtProviderTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");
    private static readonly TimeSpan PastGrace =
        SongRequestQueueStore.InFlightCheckInterval + TimeSpan.FromSeconds(1);

    private sealed class Harness
    {
        public required MusicService Sut { get; init; }
        public required SongRequestQueueStore Store { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required RecordingEventBus Events { get; init; }
        public required List<string> Pushed { get; init; }

        /// <summary>What the provider's queue read answers; null = "cannot tell".</summary>
        public IReadOnlyList<TrackInfo>? ProviderQueue { get; set; } = [];
        public TrackInfo? CurrentTrack { get; set; }

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

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-24T16:41:00Z"));
        SongRequestQueueStore store = new(clock);
        RecordingEventBus events = new();
        List<string> pushed = [];
        IMusicProvider provider = Substitute.For<IMusicProvider>();

        MusicService sut = new(
            [provider],
            db,
            events,
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor()
        );

        Harness harness = new()
        {
            Sut = sut,
            Store = store,
            Clock = clock,
            Events = events,
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
            .Returns(_ => Task.FromResult(harness.ProviderQueue));
        provider
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(_ => Task.FromResult(harness.CurrentTrack));

        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("anda_six", Entry("spotify:track:family", "family ties", "anda_six"));
        queue.Enqueue("qtkitte", Entry("spotify:track:rebirth", "The First Rebirth", "QTkittE"));
        return harness;
    }

    private static SongRequestEntry Entry(string uri, string name, string requestedBy) =>
        new(uri, name, "Artist", null, 200_000, requestedBy, 0, null, "");

    private static TrackInfo Track(string uri) =>
        new()
        {
            TrackName = uri,
            Artist = "Artist",
            Album = "Album",
            TrackUri = uri,
            Provider = "spotify",
        };

    /// <summary>Hands over the head, then lets the recovery tick run once the grace has passed.</summary>
    private static async Task HandOverThenTickPastGraceAsync(Harness h)
    {
        await h.Sut.HandOverNextAsync(h.Channel);
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
        h.Clock.Advance(PastGrace);
        await h.Sut.HandOverNextAsync(h.Channel);
    }

    [Fact]
    public async Task A_request_removed_from_the_provider_queue_is_dropped_and_the_next_one_is_sent()
    {
        Harness h = Build();
        h.ProviderQueue = []; // the streamer deleted it in the Spotify app

        await HandOverThenTickPastGraceAsync(h);

        h.Pushed.Should().Equal("spotify:track:family", "spotify:track:rebirth");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("The First Rebirth");
        h.Store.TryGet(h.Channel)!
            .GetSnapshot()
            .Select(e => e.Item.TrackName)
            .Should()
            .Equal("The First Rebirth");

        SongRequestLostAtProviderEvent lost = h
            .Events.Published.OfType<SongRequestLostAtProviderEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        lost.BroadcasterId.Should().Be(ChannelId);
        lost.TrackUri.Should().Be("spotify:track:family");
        lost.TrackName.Should().Be("family ties");
        lost.RequestedBy.Should().Be("anda_six");
        h.Events.Published.OfType<SongRequestQueueChangedEvent>()
            .Should()
            .ContainSingle()
            .Which.Items.Select(i => i.Title)
            .Should()
            .Equal("The First Rebirth");
    }

    [Fact]
    public async Task Nothing_is_judged_before_the_grace_period_after_hand_over()
    {
        Harness h = Build();
        h.ProviderQueue = [];

        await h.Sut.HandOverNextAsync(h.Channel);
        h.Clock.Advance(SongRequestQueueStore.InFlightCheckInterval - TimeSpan.FromSeconds(1));
        await h.Sut.HandOverNextAsync(h.Channel);

        h.Pushed.Should().Equal("spotify:track:family");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
        h.Events.Published.OfType<SongRequestLostAtProviderEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unanswerable_provider_queue_read_keeps_the_request()
    {
        Harness h = Build();
        h.ProviderQueue = null;

        await HandOverThenTickPastGraceAsync(h);

        h.Pushed.Should().Equal("spotify:track:family");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
        h.Events.Published.OfType<SongRequestLostAtProviderEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_still_waiting_in_the_provider_queue_is_kept()
    {
        Harness h = Build();
        h.ProviderQueue = [Track("spotify:track:other"), Track("spotify:track:family")];

        await HandOverThenTickPastGraceAsync(h);

        h.Pushed.Should().Equal("spotify:track:family");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
    }

    [Fact]
    public async Task A_full_look_ahead_proves_nothing_so_the_request_is_kept()
    {
        Harness h = Build();
        h.ProviderQueue = [.. Enumerable.Range(0, 20).Select(i => Track($"spotify:track:hand{i}"))];

        await HandOverThenTickPastGraceAsync(h);

        h.Pushed.Should().Equal("spotify:track:family");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
    }

    [Fact]
    public async Task A_request_that_is_playing_right_now_is_kept()
    {
        Harness h = Build();
        h.ProviderQueue = [];
        h.CurrentTrack = Track("spotify:track:family");

        await HandOverThenTickPastGraceAsync(h);

        h.Pushed.Should().Equal("spotify:track:family");
        h.Store.GetInFlight(h.Channel)!.TrackName.Should().Be("family ties");
    }

    [Fact]
    public async Task The_chat_notice_names_the_requester_and_the_song()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        List<string> sent = [];
        chat.SendMessageAsync(ChannelId, Arg.Do<string>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(true);

        await new SongRequestLostAtProviderChatNotice(chat).HandleAsync(
            new()
            {
                BroadcasterId = ChannelId,
                TrackUri = "spotify:track:family",
                TrackName = "family ties",
                RequestedBy = "anda_six",
            }
        );

        sent.Should().ContainSingle();
        sent[0].Should().StartWith("@anda_six ").And.Contain("\"family ties\"");
    }
}
