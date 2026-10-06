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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// 2026-10-06: a YouTube link typed while Spotify was active fell through to a Spotify text search of the
/// raw URL and queued an unrelated song. A link the active provider cannot resolve is never searched as
/// text: a YouTube link is searched by its title, anything else is refused.
/// </summary>
public sealed class MusicServiceForeignLinkTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007813");

    private const string YouTubeLink =
        "https://www.youtube.com/watch?v=Kg8NeUI-aQQ&list=RDKg8NeUI-aQQ&start_radio=1";

    private const string TitleHit = "Zanzibar - Hakuna Matata";

    [Fact]
    public async Task A_youtube_link_with_a_title_is_searched_by_the_title_and_that_hit_is_queued()
    {
        IMusicProvider provider = Provider(hit: Track("Hakuna Matata", "spotify:track:hakuna"));
        IForeignLinkTitleLookup titles = Titles(YouTubeLink, TitleHit);
        MusicService sut = Build(provider, titles);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeLink,
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Uri.Should().Be("spotify:track:hakuna");
        await provider
            .Received(1)
            .SearchAsync(ChannelId, TitleHit, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await provider
            .DidNotReceive()
            .SearchAsync(
                Arg.Any<Guid>(),
                Arg.Is<string>(q => q.Contains("youtube.com")),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        (await QueuedNames(sut)).Should().Equal("Hakuna Matata");
    }

    [Fact]
    public async Task A_youtube_link_without_a_title_is_refused_and_nothing_is_searched_or_queued()
    {
        IMusicProvider provider = Provider(hit: Track("Schudden", "spotify:track:schudden"));
        MusicService sut = Build(provider, Titles(YouTubeLink, null));

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeLink,
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNSUPPORTED_LINK");
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default);
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_non_youtube_foreign_link_is_refused_and_nothing_is_searched_or_queued()
    {
        const string link = "https://soundcloud.com/some-artist/some-track";
        IMusicProvider provider = Provider(hit: Track("Schudden", "spotify:track:schudden"));
        MusicService sut = Build(provider, Titles(link, null));

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            link,
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNSUPPORTED_LINK");
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default);
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddToQueue_with_a_foreign_link_queues_nothing()
    {
        const string link = "https://soundcloud.com/some-artist/some-track";
        IMusicProvider provider = Provider(hit: Track("Schudden", "spotify:track:schudden"));
        MusicService sut = Build(provider, Titles(link, null));

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), link, "viewer1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNSUPPORTED_LINK");
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default);
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_the_provider_resolves_is_queued_without_any_title_lookup()
    {
        const string link = "https://open.spotify.com/track/abc123";
        IMusicProvider provider = Provider(hit: null);
        provider
            .ResolveTrackAsync(ChannelId, link, Arg.Any<CancellationToken>())
            .Returns(
                (Track("Resolved Song", "spotify:track:abc123"), MusicProviderFailureReason.None)
            );
        IForeignLinkTitleLookup titles = Substitute.For<IForeignLinkTitleLookup>();
        MusicService sut = Build(provider, titles);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            link,
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Uri.Should().Be("spotify:track:abc123");
        await titles.DidNotReceiveWithAnyArgs().TryGetTitleAsync(default!, default);
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default);
        (await QueuedNames(sut)).Should().Equal("Resolved Song");
    }

    [Fact]
    public async Task A_plain_phrase_is_searched_as_typed_without_any_title_lookup()
    {
        IMusicProvider provider = Provider(
            hit: Track("Never Gonna Give You Up", "spotify:track:rick")
        );
        IForeignLinkTitleLookup titles = Substitute.For<IForeignLinkTitleLookup>();
        MusicService sut = Build(provider, titles);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        await provider
            .Received(1)
            .SearchAsync(
                ChannelId,
                "never gonna give you up",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await titles.DidNotReceiveWithAnyArgs().TryGetTitleAsync(default!, default);
        (await QueuedNames(sut)).Should().Equal("Never Gonna Give You Up");
    }

    private static async Task<List<string>> QueuedNames(MusicService sut) =>
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Select(i => i.TrackName).ToList();

    private static TrackInfo Track(string name, string uri) =>
        new()
        {
            TrackName = name,
            Artist = "Artist",
            Album = "Album",
            TrackUri = uri,
            Provider = "spotify",
            DurationMs = 180_000,
        };

    private static IForeignLinkTitleLookup Titles(string link, string? title)
    {
        IForeignLinkTitleLookup titles = Substitute.For<IForeignLinkTitleLookup>();
        titles.TryGetTitleAsync(link, Arg.Any<CancellationToken>()).Returns(title);
        return titles;
    }

    private static IMusicProvider Provider(TrackInfo? hit)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .ResolveTrackAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((null, MusicProviderFailureReason.None));
        provider
            .AddToQueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IReadOnlyList<TrackInfo> hits = hit is null ? [] : [hit];
        provider
            .SearchAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((hits, MusicProviderFailureReason.None));
        return provider;
    }

    private static MusicService Build(IMusicProvider provider, IForeignLinkTitleLookup titles)
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

        return new MusicService(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            titles
        );
    }
}
