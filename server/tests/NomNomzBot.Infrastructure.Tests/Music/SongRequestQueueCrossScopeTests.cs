// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S001 — proves the song-request queue is a live, shared record instead of a fiction that resets on
/// every request. Before this fix <see cref="MusicService"/> held <c>_queues</c> as an instance field,
/// and the service is registered SCOPED (one instance per HTTP request / chat-command dispatch) — so
/// a viewer's <c>!sr</c> populated one throwaway MusicService's private dictionary, and the very next
/// <c>!queue</c> or <c>GET /queue</c> call got a brand-new MusicService with an empty dictionary. Every
/// test here builds TWO separate <see cref="MusicService"/> instances — one per simulated DI scope —
/// sharing only the singleton <see cref="ISongRequestQueueStore"/>, exactly as the real container would
/// hand out a new scoped MusicService per request while reusing the one singleton store. A single-scope
/// test cannot catch this class of bug by construction, which is why none of these instantiate the
/// service only once.
/// </summary>
public sealed class SongRequestQueueCrossScopeTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000f1001");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000f1002");

    [Fact]
    public async Task Add_in_one_scope_is_visible_to_chat_read_and_http_read_in_separate_scopes()
    {
        ISongRequestQueueStore store = new SongRequestQueueStore();
        MusicTestDbContext db = SeedChannel(ChannelA);

        // Scope 1: the chat dispatch that handles `!sr`.
        MusicService writeScope = BuildScope(db, store);
        Result added = await writeScope.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:aaa1",
            "viewer1"
        );
        added.IsSuccess.Should().BeTrue();

        // Scope 2: the chat dispatch that handles the very next `!queue` — a fresh MusicService.
        MusicService chatReadScope = BuildScope(db, store);
        MusicQueue chatView = await chatReadScope.GetQueueAsync(ChannelA.ToString());

        // Scope 3: the HTTP request behind `GET /queue` — yet another fresh MusicService.
        MusicService httpReadScope = BuildScope(db, store);
        MusicQueue httpView = await httpReadScope.GetQueueAsync(ChannelA.ToString());

        chatView.Queue.Should().ContainSingle().Which.TrackName.Should().Be("Track aaa1");
        httpView.Queue.Should().ContainSingle().Which.TrackName.Should().Be("Track aaa1");
        // All three scopes describe the exact same queue content — not merely a non-empty count.
        chatView.Queue.Should().BeEquivalentTo(httpView.Queue);
    }

    [Fact]
    public async Task One_channels_queue_never_leaks_into_another_channels_queue()
    {
        ISongRequestQueueStore store = new SongRequestQueueStore();
        MusicTestDbContext db = SeedChannel(ChannelA);
        SeedChannel(ChannelB, db);

        MusicService scopeForA = BuildScope(db, store);
        Result added = await scopeForA.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:aaa1",
            "viewer1"
        );
        added.IsSuccess.Should().BeTrue();

        // A different scope, reading channel B, must never see channel A's request.
        MusicService scopeForB = BuildScope(db, store);
        MusicQueue channelBView = await scopeForB.GetQueueAsync(ChannelB.ToString());
        channelBView.Queue.Should().BeEmpty();

        // Channel A's own read (yet another scope) still sees its own entry.
        MusicService scopeForAAgain = BuildScope(db, store);
        MusicQueue channelAView = await scopeForAAgain.GetQueueAsync(ChannelA.ToString());
        channelAView.Queue.Should().ContainSingle().Which.TrackName.Should().Be("Track aaa1");
    }

    [Fact]
    public async Task Concurrent_adds_from_two_scopes_both_land_with_no_lost_entry()
    {
        ISongRequestQueueStore store = new SongRequestQueueStore();
        MusicTestDbContext db = SeedChannel(ChannelA);

        MusicService scope1 = BuildScope(db, store);
        MusicService scope2 = BuildScope(db, store);

        Task<Result> firstAdd = scope1.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:aaa1",
            "viewer1"
        );
        Task<Result> secondAdd = scope2.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:bbb2",
            "viewer2"
        );
        Result[] results = await Task.WhenAll(firstAdd, secondAdd);

        results.Should().OnlyContain(r => r.IsSuccess);

        MusicService readScope = BuildScope(db, store);
        MusicQueue finalView = await readScope.GetQueueAsync(ChannelA.ToString());

        finalView.Queue.Should().HaveCount(2);
        finalView
            .Queue.Select(q => q.TrackName)
            .Should()
            .BeEquivalentTo("Track aaa1", "Track bbb2");
    }

    /// <summary>Builds a fresh <see cref="MusicService"/> standing in for one DI scope — a new instance
    /// every time, exactly like the container handing out a new scoped instance per request — wired to
    /// the SHARED singleton queue store passed in by the caller.</summary>
    private static MusicService BuildScope(MusicTestDbContext db, ISongRequestQueueStore store) =>
        new(
            [
                new SpotifyMusicProvider(
                    db,
                    new FakeIntegrationTokenVault(db),
                    new InMemoryIntegrationCapabilityStore(),
                    new LastActiveSpotifyDeviceTracker(),
                    new SingleHandlerClientFactory(new TrackEchoSpotifyHandler()),
                    TimeProvider.System,
                    NullLogger<SpotifyMusicProvider>.Instance,
                    NullSystemCredentialsProvider.Instance,
                    new ConnectionRefreshGate()
                ),
            ],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore()
        );

    private static MusicTestDbContext SeedChannel(Guid channelId, MusicTestDbContext? into = null)
    {
        MusicTestDbContext db =
            into
            ?? new MusicTestDbContext(
                new DbContextOptionsBuilder<MusicTestDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
        // Routing seed: MusicService.GetActiveProviderAsync selects the active provider by which
        // Service names are connected — a separate concern from SpotifyMusicProvider's OWN token
        // resolution (which reads the vault, S003; a fresh FakeIntegrationTokenVault is built per
        // BuildScope call above, over this same db, so it always finds the connection seeded here).
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = channelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();
        new FakeIntegrationTokenVault(db).SeedConnectedSpotify(channelId);
        return db;
    }

    /// <summary>Resolves any <c>spotify:track:{id}</c> to a track named after its id (so two concurrently
    /// requested ids are distinguishable in the resulting queue snapshot) and answers every other Spotify
    /// call (currently-playing, queue push) with 204 No Content.</summary>
    private sealed class TrackEchoSpotifyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            int tracksIndex = path.IndexOf("/tracks/", StringComparison.Ordinal);
            if (tracksIndex >= 0)
            {
                string id = path[(tracksIndex + "/tracks/".Length)..];
                string json =
                    """{"id":"ID","name":"Track ID","uri":"spotify:track:ID","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}""".Replace(
                        "ID",
                        id,
                        StringComparison.Ordinal
                    );
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    }
                );
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
