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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-SR-STALE — owner report (live stream, 2026-09-04): <c>!sr</c> answered the SECOND request in a pair
/// with the FIRST request's resolved track ("!sr joliene" → Jolène/Måneskin; "!sr 9 to 5" → Jolene/Dolly
/// Parton, which is what "joliene" alone resolves to on Spotify's real API — confirmed against the live
/// endpoint). Each of these proves one back-to-back pair of <see cref="MusicService.RequestTrackAsync"/>
/// calls resolves its OWN query — never the query that came immediately before it — across the three
/// shapes a one-behind bug could hide in: two different viewers, the same viewer twice, and (as the
/// control case) a genuine repeat that the product deliberately refuses (<c>DUPLICATE_TRACK</c>,
/// <see cref="MusicService.EnqueueResolvedAsync"/>'s "legacy-parity duplicate gate").
/// </summary>
public sealed class MusicServiceSequentialRequestsTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007c01");

    private const string JolieneQuery = "joliene";
    private const string JolieneTrackJson = """
        {"tracks":{"items":[{"name":"Jolene","uri":"spotify:track:2SpEHTbUuebeLkgs9QB7Ue","duration_ms":161960,"artists":[{"name":"Dolly Parton"}],"album":{"name":"Jolene","images":[]}}]}}
        """;

    private const string NineToFiveQuery = "9 to 5";
    private const string NineToFiveTrackJson = """
        {"tracks":{"items":[{"name":"9 to 5","uri":"spotify:track:4w3tQBXhn5345eUXDGBWZG","duration_ms":162000,"artists":[{"name":"Dolly Parton"}],"album":{"name":"9 to 5","images":[]}}]}}
        """;

    [Fact]
    public async Task Two_different_queries_from_the_same_viewer_each_resolve_their_own_track()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RouteSearchByQuery(handler, JolieneQuery, JolieneTrackJson);
        RouteSearchByQuery(handler, NineToFiveQuery, NineToFiveTrackJson);
        RouteAdmissionProbesAndPush(handler);

        Result<MusicTrack> first = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            JolieneQuery,
            "viewer1"
        );
        Result<MusicTrack> second = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            NineToFiveQuery,
            "viewer1"
        );

        first.IsSuccess.Should().BeTrue();
        first.Value.Uri.Should().Be("spotify:track:2SpEHTbUuebeLkgs9QB7Ue");
        first.Value.Name.Should().Be("Jolene");

        // This is the exact failure the owner hit: before the fix, the second reply carried the FIRST
        // request's track (the "joliene" hit) instead of resolving "9 to 5" on its own.
        second.IsSuccess.Should().BeTrue();
        second
            .Value.Uri.Should()
            .Be(
                "spotify:track:4w3tQBXhn5345eUXDGBWZG",
                "the second request's own query must decide its own track, never the previous request's"
            );
        second.Value.Name.Should().Be("9 to 5");
    }

    [Fact]
    public async Task Two_different_queries_from_two_different_viewers_each_resolve_their_own_track()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RouteSearchByQuery(handler, JolieneQuery, JolieneTrackJson);
        RouteSearchByQuery(handler, NineToFiveQuery, NineToFiveTrackJson);
        RouteAdmissionProbesAndPush(handler);

        Result<MusicTrack> first = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            JolieneQuery,
            "viewer1"
        );
        Result<MusicTrack> second = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            NineToFiveQuery,
            "viewer2"
        );

        first.Value.Uri.Should().Be("spotify:track:2SpEHTbUuebeLkgs9QB7Ue");
        second
            .Value.Uri.Should()
            .Be(
                "spotify:track:4w3tQBXhn5345eUXDGBWZG",
                "a different requester's later query must not inherit the previous requester's resolve"
            );
    }

    [Fact]
    public async Task A_genuine_repeat_request_for_the_same_track_by_the_same_viewer_is_refused_as_a_duplicate()
    {
        // The product's documented intent for a REAL repeat (MusicService.EnqueueResolvedAsync's
        // "Duplicate gate (legacy parity)" comment, and MusicServiceDuplicateRequestTests): the same track
        // already pending is refused, not queued twice, and the refusal names the original requester —
        // this must keep working once query-by-query resolution is proven above, so a stale-fix can't
        // accidentally turn a real duplicate into two queue entries.
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RouteSearchByQuery(handler, JolieneQuery, JolieneTrackJson);
        RouteAdmissionProbesAndPush(handler);

        Result<MusicTrack> first = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            JolieneQuery,
            "viewer1"
        );
        Result<MusicTrack> repeat = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            JolieneQuery,
            "viewer1"
        );

        first.IsSuccess.Should().BeTrue();
        repeat.IsFailure.Should().BeTrue();
        repeat.ErrorCode.Should().Be("DUPLICATE_TRACK");
        repeat.ErrorMessage.Should().Contain("viewer1");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    /// <summary>Routes <c>/search?q=...</c> by the DECODED query text so two different callers in the same
    /// test each get their own canned hit — a single catch-all <c>/search</c> route (as the rest of this
    /// suite uses, since it only ever exercises one query per test) cannot tell "joliene" and "9 to 5"
    /// apart, which is exactly the distinction this regression needs.</summary>
    private static void RouteSearchByQuery(
        RecordingHttpHandler handler,
        string query,
        string json
    ) =>
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)
                && Uri.UnescapeDataString(r.RequestUri.Query)
                    .Contains($"q={query}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            json
        );

    /// <summary>Nothing playing (duplicate-probe #1), an empty provider queue (duplicate-probe #2), and a
    /// successful push — the same "queue was empty, admit cleanly" shape <see cref="MusicServiceRequestTrackTests"/>
    /// uses, just shared across every request in a test instead of one.</summary>
    private static void RouteAdmissionProbesAndPush(RecordingHttpHandler handler)
    {
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.OK,
            """{"queue":[]}"""
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );
    }

    private static (MusicService Sut, RecordingHttpHandler Handler) Build()
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
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache()
        );
        return (sut, handler);
    }
}
