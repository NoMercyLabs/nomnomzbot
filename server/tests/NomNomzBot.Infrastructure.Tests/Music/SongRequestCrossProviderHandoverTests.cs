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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// music-sr.md §3.5.2: with Spotify and YouTube both on, the queue is interleaved and each item plays on
/// its own engine, one at a time. Which engine starts which item, and the stop-before-start order between
/// engines, belong to the playback director (PlaybackDirector tests); what stays here is admission's own
/// rule: a request never jumps an earlier request that still waits for its turn.
/// </summary>
public sealed class SongRequestCrossProviderHandoverTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab21");
    private const string SpotifyUri = "spotify:track:abc";
    private const string YouTubeUri = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const MusicProviderCapabilities Transport =
        MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.AcceptsSongRequests;

    private readonly IMusicProvider _spotify = Provider("spotify", SpotifyUri);
    private readonly IMusicProvider _youTube = Provider("youtube", YouTubeUri);
    private readonly SongRequestQueueStore _store = new();

    private static IMusicProvider Provider(string key, string ownedUri)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns(key);
        provider.Capabilities.Returns(Transport);
        provider.OwnsTrackUri(Arg.Any<string>()).Returns(call => (string)call[0] == ownedUri);
        provider
            .WorksWithoutConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(key == "youtube");
        return provider;
    }

    private static TrackInfo Playing(string uri, bool isPlaying, int progressMs = 196_000) =>
        new()
        {
            TrackName = "t",
            Artist = "a",
            Album = "",
            TrackUri = uri,
            Provider = "x",
            IsPlaying = isPlaying,
            DurationMs = 200_000,
            ProgressMs = progressMs,
        };

    private static SongRequestEntry Entry(string uri) =>
        new(uri, "Track", "Artist", null, 200_000, "viewer", 0, null, "");

    private MusicService NewService()
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
        return new(
            [_spotify, _youTube],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            _store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(_store).Registry,
            DirectorRig.Of(_store).Registry
        );
    }

    [Fact]
    public async Task A_new_spotify_request_never_jumps_a_youtube_request_that_waits_its_turn()
    {
        // Live 2026-10-18: the YouTube head waited for Spotify's song to end; the next Spotify request
        // was started at once, played first, and the reconciler then dropped the head.
        _store.GetOrCreate(ChannelId.ToString()).Enqueue("viewer", Entry(YouTubeUri));
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", true, progressMs: 50_000));
        _spotify
            .ResolveTrackAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>())
            .Returns((Playing(SpotifyUri, false), MusicProviderFailureReason.None));

        Result admitted = await NewService()
            .AddToQueueAsync(ChannelId.ToString(), SpotifyUri, "second-viewer");

        admitted.IsSuccess.Should().BeTrue();
        DirectorRig rig = DirectorRig.Of(_store);
        rig.Engines.EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .BeEmpty("the waiting YouTube head is first; the Spotify request starts nothing");
        rig.Engines.EngineOf(ChannelId, EngineKind.YouTube)
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .BeEmpty();
        _store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        List<string> pending =
        [
            .. _store.GetOrCreate(ChannelId.ToString()).GetSnapshot().Select(e => e.Item.TrackUri),
        ];
        pending.Should().Equal(YouTubeUri, SpotifyUri);
    }
}
