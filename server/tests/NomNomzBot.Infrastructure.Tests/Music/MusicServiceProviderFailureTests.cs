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
/// A YouTube request on a bot with no <c>YouTube:ApiKey</c> is an operator problem, not a broken
/// streamer connection: it fails with its own code and a message naming who can fix it. A provider
/// that really is disconnected keeps the reconnect message.
/// </summary>
public sealed class MusicServiceProviderFailureTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007811");

    private const string NotConfiguredMessage =
        "YouTube song requests are not set up on this bot yet. The bot owner must add a YouTube API key.";

    [Fact]
    public async Task Text_request_without_a_youtube_api_key_fails_as_not_configured()
    {
        RecordingHttpHandler handler = new();
        MusicService sut = Build(YouTubeProviderFactory.Create(null, handler), "youtube");

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_CONFIGURED");
        result.ErrorMessage.Should().Be(NotConfiguredMessage);
        handler.RequestUrls.Should().BeEmpty("no key means no call to YouTube");
    }

    [Fact]
    public async Task Link_request_without_a_youtube_api_key_fails_as_not_configured()
    {
        RecordingHttpHandler handler = new();
        MusicService sut = Build(YouTubeProviderFactory.Create(null, handler), "youtube");

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_CONFIGURED");
        result.ErrorMessage.Should().Be(NotConfiguredMessage);
        handler.RequestUrls.Should().BeEmpty("no key means no call to YouTube");
    }

    [Fact]
    public async Task A_disconnected_provider_keeps_the_reconnect_message()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("youtube");
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
            .Returns(((IReadOnlyList<TrackInfo>)[], MusicProviderFailureReason.NotConnected));
        MusicService sut = Build(provider, "youtube");

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
        result.ErrorMessage.Should().Be("The music connection needs to be reconnected.");
    }

    private static MusicService Build(IMusicProvider provider, string serviceName)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = serviceName,
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
            Substitute.For<IForeignLinkTitleLookup>()
        );
    }
}
