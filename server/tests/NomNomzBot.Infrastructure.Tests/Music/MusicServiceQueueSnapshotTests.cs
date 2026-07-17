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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves every fair-queue mutation publishes <see cref="SongRequestQueueChangedEvent"/> carrying the FRESH
/// top-of-queue snapshot — add pushes the new entry, remove and skip-dequeue push the shrunken list — so the
/// standing <c>sr_queue</c> overlay widget re-renders from the event alone. Exercises the real
/// <see cref="SpotifyMusicProvider"/> path with only the HTTP transport stubbed, mirroring
/// <see cref="MusicServicePlaybackPublishTests"/>.
/// </summary>
public sealed class MusicServiceQueueSnapshotTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f0002");

    [Fact]
    public async Task AddToQueue_publishes_the_snapshot_with_the_new_request()
    {
        (MusicService sut, RecordingEventBus bus) = Build();

        bool ok = await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");

        ok.Should().BeTrue();
        SongRequestQueueChangedEvent changed = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Single();
        changed.BroadcasterId.Should().Be(ChannelId);
        changed
            .Items.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new SongRequestQueueSnapshotItem("Song Q", "viewer1", 200));
        // The accepted-request fact still rides its own event alongside the snapshot.
        bus.Published.OfType<SongRequestedEvent>().Single().TrackName.Should().Be("Song Q");
    }

    [Fact]
    public async Task RemoveFromQueue_publishes_the_shrunken_snapshot()
    {
        (MusicService sut, RecordingEventBus bus) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");

        bool removed = await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0);

        removed.Should().BeTrue();
        SongRequestQueueChangedEvent last = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Last();
        last.Items.Should().BeEmpty();
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveFromQueue_on_an_empty_queue_publishes_nothing()
    {
        (MusicService sut, RecordingEventBus bus) = Build();

        bool removed = await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0);

        removed.Should().BeFalse();
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_that_dequeues_the_next_request_publishes_the_shrunken_snapshot()
    {
        (MusicService sut, RecordingEventBus bus) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");

        (await sut.SkipAsync(ChannelId.ToString())).IsSuccess.Should().BeTrue();

        SongRequestQueueChangedEvent last = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Last();
        last.Items.Should().BeEmpty();
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(2);
    }

    private static (MusicService Sut, RecordingEventBus Bus) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Services.Add(
            new Service
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleHandlerClientFactory(new QueueFakeSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );

        RecordingEventBus bus = new();
        MusicService sut = new([spotify], db, bus, NullLogger<MusicService>.Instance);
        return (sut, bus);
    }

    /// <summary>Stubs the Spotify surface these flows touch: search returns one canned track (the request the
    /// fair queue records), currently-playing returns "nothing playing" (204), every mutation returns 204.</summary>
    private sealed class QueueFakeSpotifyHandler : HttpMessageHandler
    {
        private const string SearchJson = """
            {"tracks":{"items":[{"name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isSearch = request.RequestUri!.AbsolutePath.EndsWith(
                "/search",
                StringComparison.Ordinal
            );

            HttpResponseMessage response = isSearch
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SearchJson, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
            return Task.FromResult(response);
        }
    }
}
