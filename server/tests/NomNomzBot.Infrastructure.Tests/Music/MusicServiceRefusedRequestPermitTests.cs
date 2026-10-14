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
using Microsoft.Extensions.Time.Testing;
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
/// A viewer's request held by the length rule is remembered for a short while, so a moderator or the
/// broadcaster can admit it with <c>!permit</c>. Only the rule that refused it is skipped: the blocklist
/// still wins, and nothing is admitted once the memory expires.
/// </summary>
public sealed class MusicServiceRefusedRequestPermitTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007814");
    private const string Viewer = "finwo_";
    private const string GhostLoveScore = "spotify:track:ghost-love-score";

    [Fact]
    public async Task A_request_held_for_length_is_queued_for_the_original_viewer_once_permitted()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);

        Result<MusicTrack> refused = await f.Music.RequestTrackAsync(
            ChannelId.ToString(),
            "nightwish - ghost love score",
            Viewer,
            requesterRoleLevel: 0,
            requesterUserId: "tw-finwo"
        );
        (await f.Queue()).Should().BeEmpty();

        Result<MusicTrack> admitted = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            Viewer
        );

        refused.ErrorCode.Should().Be("TRACK_TOO_LONG");
        admitted.IsSuccess.Should().BeTrue();
        admitted.Value.Uri.Should().Be(GhostLoveScore);
        List<(string Track, string? RequestedBy)> queued = await f.Queue();
        queued.Should().Equal(("Ghost Love Score", Viewer));
    }

    [Fact]
    public async Task The_permit_is_used_up_by_one_admission()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);
        await f.Music.RequestTrackAsync(ChannelId.ToString(), "ghost", Viewer, 0);
        await f.Music.AdmitPermittedRequestAsync(ChannelId.ToString(), Viewer);

        Result<MusicTrack> again = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            Viewer
        );

        again.IsFailure.Should().BeTrue();
        again.ErrorCode.Should().Be("NO_PENDING_REQUEST");
        (await f.Queue()).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_track_blocked_after_it_was_held_stays_refused_even_with_permission()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);
        await f.Music.RequestTrackAsync(ChannelId.ToString(), "ghost", Viewer, 0);
        await new BlockedTrackService(f.Db).BlockAsync(
            ChannelId,
            new("spotify", GhostLoveScore, "Ghost Love Score")
        );

        Result<MusicTrack> admitted = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            Viewer
        );

        admitted.IsFailure.Should().BeTrue();
        admitted.ErrorCode.Should().Be("TRACK_BLOCKED");
        (await f.Queue()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_refused_for_the_blocklist_is_never_remembered()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);
        await new BlockedTrackService(f.Db).BlockAsync(
            ChannelId,
            new("spotify", GhostLoveScore, "Ghost Love Score")
        );
        await f.Music.RequestTrackAsync(ChannelId.ToString(), "ghost", Viewer, 0);

        Result<MusicTrack> admitted = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            Viewer
        );

        admitted.ErrorCode.Should().Be("NO_PENDING_REQUEST");
        (await f.Queue()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_held_request_is_forgotten_after_fifteen_minutes()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);
        await f.Music.RequestTrackAsync(ChannelId.ToString(), "ghost", Viewer, 0);

        f.Clock.Advance(TimeSpan.FromMinutes(16));
        Result<MusicTrack> admitted = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            Viewer
        );

        admitted.ErrorCode.Should().Be("NO_PENDING_REQUEST");
        (await f.Queue()).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_admitted_for_a_viewer_who_has_no_held_request()
    {
        Fixture f = Fixture.Build(durationMs: 11 * 60_000);
        await f.Music.RequestTrackAsync(ChannelId.ToString(), "ghost", Viewer, 0);

        Result<MusicTrack> admitted = await f.Music.AdmitPermittedRequestAsync(
            ChannelId.ToString(),
            "someone_else"
        );

        admitted.ErrorCode.Should().Be("NO_PENDING_REQUEST");
        (await f.Queue()).Should().BeEmpty();
    }

    private sealed record Fixture(MusicService Music, MusicTestDbContext Db, FakeTimeProvider Clock)
    {
        public async Task<List<(string Track, string? RequestedBy)>> Queue() =>
            (await Music.GetQueueAsync(ChannelId.ToString()))
                .Queue.Select(i => (i.TrackName, i.RequestedBy))
                .ToList();

        public static Fixture Build(int durationMs)
        {
            IMusicProvider provider = Substitute.For<IMusicProvider>();
            provider.Provider.Returns("spotify");
            provider
                .ResolveTrackAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((null, MusicProviderFailureReason.None));
            provider
                .AddToQueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);
            TrackInfo hit = new()
            {
                TrackName = "Ghost Love Score",
                Artist = "Nightwish",
                Album = "Once",
                TrackUri = GhostLoveScore,
                Provider = "spotify",
                DurationMs = durationMs,
            };
            provider
                .SearchAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(((IReadOnlyList<TrackInfo>)[hit], MusicProviderFailureReason.None));

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

            FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero));
            MusicService music = new(
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
                Substitute.For<IForeignLinkTitleLookup>(),
                new RefusedSongRequestStore(clock)
            );
            return new(music, db, clock);
        }
    }
}
