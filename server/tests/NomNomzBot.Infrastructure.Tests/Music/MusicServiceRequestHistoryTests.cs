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
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using RequestRecord = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the song-request HISTORY written at the one admission point every SR flow funnels through
/// (<c>EnqueueResolvedAsync</c>). The live queue table is a snapshot — it deletes and re-inserts the
/// channel's whole row set on every change — so it can never answer "what has this viewer requested
/// before". The modiversary celebration is built on exactly that question, so an accepted request has
/// to leave a durable per-viewer trace of its own.
/// </summary>
public sealed class MusicServiceRequestHistoryTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000bb01");

    [Fact]
    public async Task An_accepted_request_records_the_track_against_the_requesters_twitch_id()
    {
        (MusicService sut, MusicTestDbContext db) = Build();

        Result<MusicTrack> requested = await sut.RequestTrackAsync(
            Channel.ToString(),
            "song q",
            requestedBy: "Viewer One",
            requesterRoleLevel: null,
            requesterUserId: "42660213"
        );

        requested.IsSuccess.Should().BeTrue();

        RequestRecord row = await db.Records.SingleAsync();
        row.RecordType.Should().Be("SongRequest");
        // Keyed on the TWITCH id, not the display name: a modiversary notice identifies the mod by
        // chatter_user_id, and a display name can change under the history it is supposed to own.
        row.UserId.Should().Be("42660213");
        row.BroadcasterId.Should().Be(Channel);

        // The resolved track is stored WITH its metadata. The legacy bot stored only a bare song id, so
        // reading a viewer's top track back needed a provider round-trip to say what it even was — and
        // said nothing at all once a track was delisted.
        JsonElement data = JsonSerializer.Deserialize<JsonElement>(row.Data);
        data.GetProperty("trackUri").GetString().Should().Be("spotify:track:q1");
        data.GetProperty("trackName").GetString().Should().Be("Song Q");
        data.GetProperty("artist").GetString().Should().Be("Artist");
        data.GetProperty("provider").GetString().Should().Be("spotify");
    }

    [Fact]
    public async Task Every_accepted_request_appends_its_own_row_and_a_refused_one_appends_none()
    {
        // "Most-requested" is a COUNT over rows, so each acceptance has to append rather than update — one
        // shared row per viewer would tie everyone at one and make the anniversary pick arbitrary. The same
        // track twice while it is still queued is refused as a duplicate by design, and a refusal is not a
        // request: it must leave no trace, or a viewer could inflate their own count by spamming one track.
        (MusicService sut, MusicTestDbContext db) = Build();

        Result<MusicTrack> first = await sut.RequestTrackAsync(
            Channel.ToString(),
            "song q",
            requestedBy: "Viewer One",
            requesterRoleLevel: null,
            requesterUserId: "42660213"
        );
        Result<MusicTrack> duplicate = await sut.RequestTrackAsync(
            Channel.ToString(),
            "song q",
            requestedBy: "Viewer One",
            requesterRoleLevel: null,
            requesterUserId: "42660213"
        );

        first.IsSuccess.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("DUPLICATE_TRACK");

        List<RequestRecord> rows = await db.Records.ToListAsync();
        rows.Should().ContainSingle("only the accepted request is history; the refusal is not");
        rows[0].UserId.Should().Be("42660213");
        rows[0].RecordType.Should().Be("SongRequest");
    }

    [Fact]
    public async Task A_request_with_no_known_requester_writes_no_history()
    {
        // Dashboard and script callers have no viewer behind them. A row keyed on "anonymous" would
        // pool every such request under one fake viewer and corrupt the counts the celebration reads.
        (MusicService sut, MusicTestDbContext db) = Build();

        await sut.RequestTrackAsync(Channel.ToString(), "song q", requestedBy: "Dashboard");

        (await db.Records.CountAsync()).Should().Be(0);
    }

    private static (MusicService Sut, MusicTestDbContext Db) Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = Channel,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(Channel);

        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(new HistorySearchHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
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
        return (sut, db);
    }

    /// <summary>Search always resolves to the one canned track "Song Q"; everything else is a 204.</summary>
    private sealed class HistorySearchHandler : HttpMessageHandler
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
                ? new(HttpStatusCode.OK)
                {
                    Content = new StringContent(SearchJson, Encoding.UTF8, "application/json"),
                }
                : new(HttpStatusCode.NoContent);

            return Task.FromResult(response);
        }
    }
}
