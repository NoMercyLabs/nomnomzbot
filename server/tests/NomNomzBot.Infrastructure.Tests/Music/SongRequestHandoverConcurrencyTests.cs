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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A song request must be started exactly once. Live 2026-09-29 ("the queue is being repeated again"): the
/// "is anything in flight?" check and the "mark it in flight" write sat on either side of an awaited
/// provider call, so a second caller that landed inside that await saw "nothing in flight", took the SAME
/// head entry and started it again, and the track played twice. Each caller runs in its own DI scope over
/// the one singleton queue store. This test holds the first engine start open and proves a concurrent
/// admission cannot start the entry again.
/// </summary>
public sealed class SongRequestHandoverConcurrencyTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");

    private static readonly TimeSpan RaceWindow = TimeSpan.FromMilliseconds(500);

    private static MusicService NewScope(IMusicProvider provider, ISongRequestQueueStore store)
    {
        // Each scope gets its own database context, exactly as two chat commands each resolve their own
        // scoped MusicService over the one singleton queue store (and the one director registry).
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
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(store).Registry,
            DirectorRig.Of(store).Registry
        );
    }

    /// <summary>A provider that resolves no metadata, so admission degrades to a placeholder entry.</summary>
    private static IMusicProvider NewProvider()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .ResolveTrackAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((TrackInfo?)null, MusicProviderFailureReason.None));
        provider
            .SearchAsync(ChannelId, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TrackInfo>(), MusicProviderFailureReason.None));
        return provider;
    }

    [Fact]
    public async Task A_second_request_during_an_admission_start_does_not_start_anything_again()
    {
        IMusicProvider provider = NewProvider();
        SongRequestQueueStore store = new();
        MusicService firstScope = NewScope(provider, store);
        MusicService secondScope = NewScope(provider, store);
        // The director's Spotify engine start is the call held open.
        FakeEngine spotify = DirectorRig.Of(store).Engines.EngineOf(ChannelId, EngineKind.Spotify);
        TaskCompletionSource firstStartEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource releaseFirstStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        spotify.DuringStart = async _ =>
        {
            firstStartEntered.TrySetResult();
            await releaseFirstStart.Task;
        };

        // A viewer's !sr lands on an idle queue, so admission has the director start it on Spotify.
        Task<Result> firstAdmission = firstScope.AddToQueueAsync(
            ChannelId.ToString(),
            "spotify:track:once",
            "viewer1"
        );
        await firstStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A second viewer's !sr arrives, in its own scope, while that start is still in flight.
        Task<Result> secondAdmission = secondScope.AddToQueueAsync(
            ChannelId.ToString(),
            "spotify:track:second",
            "viewer2"
        );
        await Task.WhenAny(secondAdmission, Task.Delay(RaceWindow));

        releaseFirstStart.SetResult();
        (await firstAdmission).IsSuccess.Should().BeTrue();
        (await secondAdmission).IsSuccess.Should().BeTrue();

        spotify
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .HaveCount(
                1,
                "one request is started once; a second start makes Spotify play it twice"
            );
        await provider
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be("spotify:track:once");
        store
            .TryGet(ChannelId.ToString())!
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .Equal(
                ["spotify:track:once", "spotify:track:second"],
                "the second request still waits in our queue behind the one that started"
            );
    }
}
