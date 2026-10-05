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
/// The old bot refused song requests from a person who had banned 10 or more songs (SongRequest.cs
/// CheckUserBannedSongs). A person below that is not refused, and the refused request never reaches the
/// music provider.
/// </summary>
public sealed class MusicServiceBanStrikeGateTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007812");

    [Fact]
    public async Task A_person_with_ten_bans_is_refused_before_the_provider_is_asked()
    {
        IMusicProvider provider = Provider();
        MusicService sut = Build(provider, bansByModOne: SongBanStrikes.RequestsRefusedFrom);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer1",
            requesterUserId: "mod-1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SR_REVOKED");
        result.ErrorMessage.Should().Be("Stop requesting songs, your permission has been revoked");
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default);
    }

    [Fact]
    public async Task A_person_with_nine_bans_and_a_person_with_no_bans_are_not_refused()
    {
        IMusicProvider provider = Provider();
        MusicService sut = Build(provider, bansByModOne: SongBanStrikes.RequestsRefusedFrom - 1);

        Result<MusicTrack> nineBans = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer1",
            requesterUserId: "mod-1"
        );
        Result<MusicTrack> noBans = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer2",
            requesterUserId: "someone-else"
        );

        nineBans.ErrorCode.Should().NotBe("SR_REVOKED");
        noBans.ErrorCode.Should().NotBe("SR_REVOKED");
        await provider.ReceivedWithAnyArgs(2).SearchAsync(default, default!, default);
    }

    private static IMusicProvider Provider()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .ResolveTrackAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((null, MusicProviderFailureReason.None));
        provider
            .SearchAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(((IReadOnlyList<TrackInfo>)[], MusicProviderFailureReason.None));
        return provider;
    }

    private static MusicService Build(IMusicProvider provider, int bansByModOne)
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
        for (int i = 0; i < bansByModOne; i++)
        {
            db.BlockedTracks.Add(
                new()
                {
                    BroadcasterId = ChannelId,
                    Provider = "spotify",
                    TrackUri = $"spotify:track:banned{i}",
                    Title = $"Banned {i}",
                    BlockedByUserId = "mod-1",
                }
            );
        }
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
            Substitute.For<IUserIdentityService>()
        );
    }
}
