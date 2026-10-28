// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// 2026-10-19: a fan upload of a real song (category "24") was refused as "not music". The channel's
/// YouTubeMusicCategoryOnly setting now decides; a music.youtube.com link always counts as music.
/// </summary>
public sealed class MusicServiceYouTubeMusicCategoryTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007814");

    private const string FanUploadId = "VFURd_EOluU";
    private const string FanUploadLink = "https://youtu.be/VFURd_EOluU";
    private const string MusicSiteLink = "https://music.youtube.com/watch?v=VFURd_EOluU";

    [Fact]
    public async Task A_fan_upload_outside_the_music_category_is_refused_when_the_setting_is_on()
    {
        MusicService sut = Build(categoryOnly: true, FanUpload());

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            FanUploadLink,
            "viewer1",
            requesterRoleLevel: PermissionLevel.Broadcaster.ToLevelValue()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_NOT_MUSIC");
        result
            .ErrorMessage.Should()
            .Contain("Music category")
            .And.Contain("official artist (- Topic) channel");
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_fan_upload_outside_the_music_category_is_queued_when_the_setting_is_off()
    {
        MusicService sut = Build(categoryOnly: false, FanUpload());

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            FanUploadLink,
            "viewer1",
            requesterRoleLevel: PermissionLevel.Broadcaster.ToLevelValue()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Uri.Should().Be($"https://www.youtube.com/watch?v={FanUploadId}");
        (await QueuedNames(sut)).Should().Equal("Fan upload");
    }

    [Fact]
    public async Task A_music_youtube_com_link_is_queued_even_when_the_upload_is_outside_the_category()
    {
        MusicService sut = Build(categoryOnly: true, FanUpload());

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            MusicSiteLink,
            "viewer1",
            requesterRoleLevel: PermissionLevel.Broadcaster.ToLevelValue()
        );

        result.IsSuccess.Should().BeTrue();
        (await QueuedNames(sut)).Should().Equal("Fan upload");
    }

    [Fact]
    public async Task A_search_whose_every_hit_is_outside_the_music_category_is_refused_with_the_rule()
    {
        MusicService sut = Build(categoryOnly: true, FanUpload());

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "fan upload",
            "viewer1",
            requesterRoleLevel: PermissionLevel.Broadcaster.ToLevelValue()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_NOT_MUSIC");
        result
            .ErrorMessage.Should()
            .Contain("Music category")
            .And.Contain("official artist (- Topic) channel");
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_search_with_no_hits_at_all_is_still_not_found()
    {
        MusicService sut = Build(categoryOnly: true, FanUpload(), searchHits: []);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "nothing matches this",
            "viewer1",
            requesterRoleLevel: PermissionLevel.Broadcaster.ToLevelValue()
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
        (await QueuedNames(sut)).Should().BeEmpty();
    }

    private static async Task<List<string>> QueuedNames(MusicService sut) =>
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Select(i => i.TrackName).ToList();

    private static TrackInfo FanUpload() =>
        new()
        {
            TrackName = "Fan upload",
            Artist = "tavrion",
            Album = string.Empty,
            TrackUri = $"https://www.youtube.com/watch?v={FanUploadId}",
            Provider = "youtube",
            ProviderTrackId = FanUploadId,
            DurationMs = 200_000,
            IsMusicCategory = false,
        };

    private static MusicService Build(
        bool categoryOnly,
        TrackInfo upload,
        IReadOnlyList<TrackInfo>? searchHits = null
    )
    {
        IReadOnlyList<TrackInfo> hits = searchHits ?? [upload];
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("youtube");
        provider.OwnsLink(Arg.Any<string>()).Returns(true);
        provider
            .ResolveTrackAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                call.ArgAt<string>(1).Contains(FanUploadId, StringComparison.Ordinal)
                    ? (upload, MusicProviderFailureReason.None)
                    : (null, MusicProviderFailureReason.None)
            );
        provider
            .SearchAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((hits, MusicProviderFailureReason.None));
        provider
            .AddToQueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "youtube",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new MusicConfigDto(
                        true,
                        "auto",
                        50,
                        5,
                        true,
                        true,
                        "everyone",
                        YouTubeMusicCategoryOnly: categoryOnly
                    )
                )
            );

        return new MusicService(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            DirectorRig.NewStore(out SongRequestQueueStore directorStore),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            config,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(directorStore).Registry,
            DirectorRig.Of(directorStore).Registry
        );
    }
}
