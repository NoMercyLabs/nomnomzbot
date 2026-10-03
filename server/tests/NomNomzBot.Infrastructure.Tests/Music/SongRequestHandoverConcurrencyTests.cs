// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A song request must reach the provider exactly once. Live 2026-09-29 ("the queue is being repeated
/// again"): the "is anything in flight?" check and the "mark it in flight" write sat on either side of the
/// awaited Spotify POST, in both the admission push and <see cref="MusicService.HandOverNextAsync"/>. The
/// playback poller calls HandOverNextAsync every second, and the reconciler calls it again on every playback
/// change, each from its own DI scope. Any second caller that landed inside that await saw "nothing in
/// flight", peeked the SAME head entry and pushed it again, so Spotify held the track twice and played it
/// twice. These tests hold the first push open and prove a concurrent caller cannot push the entry again.
/// </summary>
public sealed class SongRequestHandoverConcurrencyTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");

    private static readonly TimeSpan RaceWindow = TimeSpan.FromMilliseconds(500);

    private static MusicService NewScope(IMusicProvider provider, ISongRequestQueueStore store)
    {
        // Each scope gets its own database context, exactly as the poller, the reconciler and a chat
        // command each resolve their own scoped MusicService over the one singleton queue store.
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
            Substitute.For<IUserIdentityService>()
        );
    }

    /// <summary>A provider whose FIRST push hangs until released; every push is recorded.</summary>
    private static IMusicProvider GatedProvider(
        ConcurrentQueue<string> pushed,
        TaskCompletionSource firstPushEntered,
        TaskCompletionSource<bool> releaseFirstPush
    )
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .ResolveTrackAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((TrackInfo?)null, MusicProviderFailureReason.None));
        provider
            .SearchAsync(ChannelId, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TrackInfo>(), MusicProviderFailureReason.None));
        provider
            .GetQueueAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TrackInfo>?>([]));
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                pushed.Enqueue(call.ArgAt<string>(1));
                if (pushed.Count > 1)
                    return Task.FromResult(true);
                firstPushEntered.TrySetResult();
                return releaseFirstPush.Task;
            });
        return provider;
    }

    private static SongRequestEntry PendingEntry(string trackUri, string requestedBy) =>
        new(trackUri, "Track", "Artist", null, 200_000, requestedBy, 0, null, "");

    [Fact]
    public async Task The_poller_recovery_tick_during_an_admission_push_does_not_push_the_same_request_again()
    {
        ConcurrentQueue<string> pushed = new();
        TaskCompletionSource firstPushEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> releaseFirstPush = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IMusicProvider provider = GatedProvider(pushed, firstPushEntered, releaseFirstPush);
        SongRequestQueueStore store = new();
        MusicService chatScope = NewScope(provider, store);
        MusicService pollerScope = NewScope(provider, store);

        // A viewer's !sr lands on an idle queue, so admission pushes it straight to Spotify.
        Task<Result> admission = chatScope.AddToQueueAsync(
            ChannelId.ToString(),
            "spotify:track:once",
            "viewer1"
        );
        await firstPushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The poller's 1s recovery tick fires while that POST is still on the wire.
        Task recoveryTick = pollerScope.HandOverNextAsync(ChannelId.ToString());
        await Task.WhenAny(recoveryTick, Task.Delay(RaceWindow));

        releaseFirstPush.SetResult(true);
        (await admission).IsSuccess.Should().BeTrue();
        await recoveryTick;

        pushed
            .Should()
            .Equal(
                ["spotify:track:once"],
                "one request is handed to Spotify once; a second push makes Spotify play it twice"
            );
        store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be("spotify:track:once");
        store.TryGet(ChannelId.ToString())!.GetSnapshot().Should().ContainSingle();
    }

    [Fact]
    public async Task Reconciler_and_poller_handing_over_at_the_same_time_push_the_head_only_once()
    {
        ConcurrentQueue<string> pushed = new();
        TaskCompletionSource firstPushEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> releaseFirstPush = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IMusicProvider provider = GatedProvider(pushed, firstPushEntered, releaseFirstPush);
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("viewer1", PendingEntry("spotify:track:head", "viewer1"));
        queue.Enqueue("viewer2", PendingEntry("spotify:track:second", "viewer2"));
        MusicService reconcilerScope = NewScope(provider, store);
        MusicService pollerScope = NewScope(provider, store);

        Task reconcilerHandover = reconcilerScope.HandOverNextAsync(ChannelId.ToString());
        await firstPushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task pollerHandover = pollerScope.HandOverNextAsync(ChannelId.ToString());
        await Task.WhenAny(pollerHandover, Task.Delay(RaceWindow));

        releaseFirstPush.SetResult(true);
        await Task.WhenAll(reconcilerHandover, pollerHandover);

        pushed
            .Should()
            .Equal(
                ["spotify:track:head"],
                "exactly one request is ever at the provider, and it is pushed once"
            );
        store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be("spotify:track:head");
        queue.GetSnapshot().Should().HaveCount(2, "the second request still waits in our queue");
    }
}
