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
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Legacy-parity duplicate gate: the same track already pending — or playing right now — is refused with a
/// reply naming who has it, instead of being queued twice and played twice. The now-playing half is
/// best-effort: a provider that cannot answer the probe must never turn a good request into a failure.
/// </summary>
public sealed class MusicServiceDuplicateRequestTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007a01");
    private const string TrackUri = "spotify:track:dup1";

    [Fact]
    public async Task The_same_track_requested_twice_is_refused_and_names_the_first_requester()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RespondQueuePush(handler, HttpStatusCode.NoContent);
        RespondNothingPlaying(handler);

        (await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        Result second = await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer2");

        second.ErrorCode.Should().Be("DUPLICATE_TRACK");
        second.ErrorMessage.Should().Contain("viewer1");
        // Refused before the provider is touched — one push, one queue entry, one play.
        handler
            .RequestUrls.Count(u => u.Contains("/me/player/queue", StringComparison.Ordinal))
            .Should()
            .Be(1);
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task Requesting_the_track_that_is_playing_right_now_is_refused()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RespondQueuePush(handler, HttpStatusCode.NoContent);
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"item":{"name":"Dup","uri":"__URI__","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}},"is_playing":true,"progress_ms":1000}
            """.Replace("__URI__", TrackUri)
        );

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer1");

        result.ErrorCode.Should().Be("DUPLICATE_TRACK");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_probe_that_cannot_answer_is_not_treated_as_a_match()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RespondQueuePush(handler, HttpStatusCode.NoContent);
        // Dead token: the provider contract turns every unanswerable probe into a null read, which must
        // not count as "this track is playing" — otherwise a broken connection refuses every request.
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.Unauthorized,
            """{"error":{"status":401,"message":"The access token expired"}}"""
        );

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer1");

        result.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    private static void RespondNothingPlaying(RecordingHttpHandler handler) =>
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

    private static void RespondQueuePush(RecordingHttpHandler handler, HttpStatusCode status) =>
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            status
        );

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
            PermissiveMusicConfigService.Instance
        );
        return (sut, handler);
    }
}
