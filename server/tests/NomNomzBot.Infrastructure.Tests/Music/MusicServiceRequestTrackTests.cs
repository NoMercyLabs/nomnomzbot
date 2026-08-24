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
/// Proves <see cref="MusicService.RequestTrackAsync"/> — the !sr / song-request entry point every free-text
/// caller (the <c>!sr</c> builtin, the reward-pipeline <c>song_request</c> action, scripts) goes through. A
/// pasted track link must resolve to its exact track via the provider's authoritative lookup (GET
/// /tracks/{id}) — NOT the provider's text search, which cannot parse a URL and previously left every link
/// request answering "no tracks found". A plain search phrase still falls through to search. Both land in
/// the fair queue; a blocked track is refused either way.
/// </summary>
public sealed class MusicServiceRequestTrackTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007701");
    private const string TrackId = "4uLU6hMCjMI75M1A2tKUQC";

    private const string TrackJson = """
        {"id":"4uLU6hMCjMI75M1A2tKUQC","name":"Never Gonna Give You Up",
         "uri":"spotify:track:4uLU6hMCjMI75M1A2tKUQC","duration_ms":213573,
         "artists":[{"name":"Rick Astley"}],
         "album":{"name":"Whenever You Need Somebody","images":[]}}
        """;

    private const string SearchJson = """
        {"tracks":{"items":[{"name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
        """;

    private const string EmptySearchJson = """{"tracks":{"items":[]}}""";

    [Fact]
    public async Task A_pasted_track_link_resolves_via_the_tracks_endpoint_not_search()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );
        // If the service ever falls through to search for a link, this 500 would fail the request —
        // proving the resolve path is what actually served it, not search.
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.InternalServerError,
            "{}"
        );
        // The queue was empty, so admission pushes the resolved track straight to the provider — the
        // push must succeed for the request as a whole to succeed (S002: a failed push is a failed
        // request, not a silently-ignored one).
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Never Gonna Give You Up");
        result.Value.Uri.Should().Be($"spotify:track:{TrackId}");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task A_search_phrase_falls_through_to_search_and_still_queues()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build();
        // No /tracks/{id} responder registered — a resolve attempt on this non-link input must not even
        // reach the API (ExtractId fails closed on multi-word text), so only /search needs to answer.
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            SearchJson
        );
        // The queue was empty, so admission pushes the resolved track straight to the provider (S002).
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "song q please",
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Song Q");
        handler
            .RequestUrls.Should()
            .Contain(url => url.Contains("/search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unresolvable_link_and_an_empty_search_both_report_NOT_FOUND()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.NotFound,
            "{}"
        );
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            EmptySearchJson
        );

        Result<MusicTrack> linkResult = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );
        Result<MusicTrack> queryResult = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "totally unknown song",
            "viewer1"
        );

        linkResult.ErrorCode.Should().Be("NOT_FOUND");
        queryResult.ErrorCode.Should().Be("NOT_FOUND");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_blocked_track_requested_by_link_is_still_refused_before_queueing()
    {
        (MusicService sut, RecordingHttpHandler handler, BlockedTrackService blocks) = Build();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );
        await blocks.BlockAsync(
            ChannelId,
            new("spotify", $"spotify:track:{TrackId}", "Never Gonna Give You Up")
        );

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.ErrorCode.Should().Be("TRACK_BLOCKED");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        MusicService Sut,
        RecordingHttpHandler Handler,
        BlockedTrackService Blocks
    ) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
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

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate()
        );

        RecordingEventBus bus = new();
        BlockedTrackService blocks = new(db);
        MusicService sut = new(
            [spotify],
            db,
            bus,
            blocks,
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore()
        );
        return (sut, handler, blocks);
    }
}
