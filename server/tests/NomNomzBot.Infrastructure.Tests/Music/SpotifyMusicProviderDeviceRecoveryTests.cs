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
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the "remember the last device used while streaming" recovery (owner requirement): a player
/// write rejected with Spotify's real 404/<c>NO_ACTIVE_DEVICE</c> transfers to the last device this
/// channel was ever observed active on, then retries the original command once — so playback never
/// fails just because nothing was selected at the moment the fair queue tries to start a song. Never
/// remembers a device unless Spotify itself reported it active, and never transfers when nothing has
/// ever been remembered (falls straight through as a normal failure).
/// </summary>
public sealed class SpotifyMusicProviderDeviceRecoveryTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");

    private const string NoActiveDeviceJson = """
        {"error":{"status":404,"reason":"NO_ACTIVE_DEVICE","message":"No active device found"}}
        """;

    [Fact]
    public async Task Play_with_no_active_device_transfers_to_the_remembered_device_then_retries()
    {
        int playAttempts = 0;
        (
            SpotifyMusicProvider sut,
            RecordingHttpHandler handler,
            ILastActiveSpotifyDeviceTracker tracker
        ) = Build();
        tracker.Remember(ChannelId, "remembered-device-id");

        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Put
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Put
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player/play", StringComparison.Ordinal)
                && playAttempts++ == 0,
            HttpStatusCode.NotFound,
            NoActiveDeviceJson
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Put
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player/play", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

        await sut.PlayAsync(ChannelId);

        playAttempts
            .Should()
            .Be(2, "the play call should have been retried once after the transfer");
        int transferIndex = handler.RequestUrls.FindIndex(u =>
            u == "PUT https://api.spotify.com/v1/me/player"
        );
        transferIndex
            .Should()
            .BeGreaterThanOrEqualTo(0, "exactly one transfer to the remembered device");
        handler.RequestBodies[transferIndex].Should().Contain("remembered-device-id");
    }

    [Fact]
    public async Task Play_with_no_active_device_and_nothing_remembered_just_fails()
    {
        (SpotifyMusicProvider sut, RecordingHttpHandler handler, _) = Build();

        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Put
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player/play", StringComparison.Ordinal),
            HttpStatusCode.NotFound,
            NoActiveDeviceJson
        );

        await sut.PlayAsync(ChannelId);

        handler
            .RequestUrls.Should()
            .ContainSingle(u => u.EndsWith("/me/player/play"), "no transfer attempted, no retry");
    }

    private static (
        SpotifyMusicProvider Sut,
        RecordingHttpHandler Handler,
        ILastActiveSpotifyDeviceTracker Tracker
    ) Build()
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

        RecordingHttpHandler handler = new();
        LastActiveSpotifyDeviceTracker tracker = new();
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            tracker,
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );
        return (spotify, handler, tracker);
    }
}
