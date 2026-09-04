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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-MUSIC-4d — <see cref="SongRequestQueueItemCode" /> was never persisted, so a restart came back with
/// every request's speakable handle silently blanked: <c>!wrongsong K7QM</c> and every other code-addressed
/// command stopped matching a restored entry, even though the in-memory path (S-MUSIC-4c) looked correct.
/// These tests prove the code round-trips through a simulated restart exactly (not merely "non-empty"),
/// stays unique against codes issued after the restore, and that the actual <c>!wrongsong</c> pipeline
/// action resolves a restored entry by its restored code.
/// </summary>
public sealed class SongRequestQueueCodeDurabilityTests
{
    private static readonly string ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000f4001")
        .ToString();

    [Fact]
    public async Task A_restored_queue_carries_the_exact_same_codes_it_had_before_the_restart()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueueStore liveStore = new();
        SongRequestQueuePersistence livePersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> live = liveStore.GetOrCreate(ChannelA);

        SongRequestEntry track1 = Entry("track-1", "viewer1", "K7QM");
        SongRequestEntry track2 = Entry("track-2", "viewer2", "N3WX");
        live.Enqueue("viewer1", track1);
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);
        live.Enqueue("viewer2", track2);
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);

        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        RestoredSongRequestQueue channel = result.Channels.Should().ContainSingle().Subject;
        restoredStore.Restore(channel.BroadcasterId, channel.OrderedEntries);

        // The restored codes EQUAL the originals — not merely non-empty, and not merely unique.
        restoredStore
            .GetOrCreate(ChannelA)
            .GetSnapshot()
            .Select(e => (e.OwnerKey, e.Item.Code))
            .Should()
            .BeEquivalentTo([("viewer1", "K7QM"), ("viewer2", "N3WX")]);
    }

    [Fact]
    public async Task Codes_stay_unique_across_a_restored_queue_plus_a_request_added_after_the_restart()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueueStore liveStore = new();
        SongRequestQueuePersistence livePersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> live = liveStore.GetOrCreate(ChannelA);

        List<string> issuedBeforeRestart = ["K7QM", "N3WX", "P9CD"];
        foreach ((string code, int i) in issuedBeforeRestart.Select((c, i) => (c, i)))
        {
            live.Enqueue($"viewer{i}", Entry($"track-{i}", $"viewer{i}", code));
            await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);
        }

        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();
        SongRequestQueuePersistence restoredPersistenceWriter = new(restartedDb);

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        RestoredSongRequestQueue channel = result.Channels.Should().ContainSingle().Subject;
        restoredStore.Restore(channel.BroadcasterId, channel.OrderedEntries);

        // A MusicService instance wired to the RESTORED store/persistence — exactly the shape a real
        // restart produces — issues the next code the same way it does for any live request
        // (MusicService.NextSongCode: taken = live queue + in-flight).
        MusicService restartedService = BuildMusicService(
            restoredStore,
            restoredPersistenceWriter,
            withProvider: true
        );
        Result added = await restartedService.AddToQueueAsync(
            ChannelA,
            "provider:track:new",
            "viewer-new"
        );
        added.IsSuccess.Should().BeTrue();

        List<string> allCodesAfter =
        [
            .. restoredStore.GetOrCreate(ChannelA).GetSnapshot().Select(e => e.Item.Code),
        ];

        allCodesAfter.Should().OnlyHaveUniqueItems();
        allCodesAfter.Should().Contain(issuedBeforeRestart);
        allCodesAfter.Should().HaveCount(4);
    }

    [Fact]
    public async Task Wrongsong_by_code_resolves_and_removes_a_restored_entry()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueueStore liveStore = new();
        SongRequestQueuePersistence livePersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> live = liveStore.GetOrCreate(ChannelA);

        SongRequestEntry mineEntry = Entry("track-mine", "Viewer", "K7QM");
        SongRequestEntry othersEntry = Entry("track-other", "SomeoneElse", "N3WX");
        live.Enqueue("Viewer", mineEntry);
        live.Enqueue("SomeoneElse", othersEntry);
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);

        // Simulated restart.
        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        RestoredSongRequestQueue channel = result.Channels.Should().ContainSingle().Subject;
        restoredStore.Restore(channel.BroadcasterId, channel.OrderedEntries);

        MusicService restartedService = BuildMusicService(restoredStore, restoredPersistence);
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        SongWrongAction action = new(restartedService, chat, NullLogger<SongWrongAction>.Instance);

        Guid broadcasterGuid = Guid.Parse(ChannelA);
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = broadcasterGuid,
            TriggeredByUserId = "u-viewer",
            TriggeredByDisplayName = "Viewer",
            MessageId = "msg-1",
            RawMessage = "!wrongsong K7QM",
        };
        ctx.Variables["args.1"] = "K7QM";

        ActionResult actionResult = await action.ExecuteAsync(ctx, new() { Type = "song_wrong" });

        actionResult.Succeeded.Should().BeTrue();
        // Resolved to THIS restored entry — its track name rides the confirmation message — never a
        // "no request matching code" failure, and never the other requester's entry.
        await chat.Received(1)
            .SendMessageAsync(
                broadcasterGuid,
                Arg.Is<string>(m => m.Contains("track-mine", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()
            );

        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> remaining = restoredStore
            .GetOrCreate(ChannelA)
            .GetSnapshot();
        remaining.Should().ContainSingle().Which.Item.Code.Should().Be("N3WX");
    }

    private static MusicService BuildMusicService(
        ISongRequestQueueStore store,
        ISongRequestQueuePersistence persistence,
        bool withProvider = false
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        IEnumerable<IMusicProvider> providers = [];
        if (withProvider)
        {
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "stub",
                    BroadcasterId = Guid.Parse(ChannelA),
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
            db.SaveChanges();
            providers = [new StubMusicProvider()];
        }

        return new(
            providers,
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            persistence,
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache()
        );
    }

    private static SongRequestEntry Entry(string trackUri, string ownerKey, string code) =>
        new(trackUri, $"Track {trackUri}", "Artist", null, 200000, ownerKey, 0, null, code);

    /// <summary>A minimal <see cref="IMusicProvider"/> that only implements what
    /// <c>MusicService.AddToQueueAsync</c>'s admission path actually calls (resolve → search fallback →
    /// duplicate probe → push) — every other member is unreachable from this test and throws, matching
    /// the "only implement what's called" convention used by the other Music test stubs.</summary>
    private sealed class StubMusicProvider : IMusicProvider
    {
        public string Provider => "stub";

        public MusicProviderCapabilities Capabilities =>
            MusicProviderCapabilities.Search | MusicProviderCapabilities.AcceptsSongRequests;

        public Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveTrackAsync(
            Guid broadcasterId,
            string uriOrId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<(TrackInfo?, MusicProviderFailureReason)>(
                (null, MusicProviderFailureReason.None)
            );

        public Task<(
            IReadOnlyList<TrackInfo> Tracks,
            MusicProviderFailureReason Failure
        )> SearchAsync(
            Guid broadcasterId,
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<(IReadOnlyList<TrackInfo>, MusicProviderFailureReason)>(
                (
                    [
                        new TrackInfo
                        {
                            TrackName = "Stub Track",
                            Artist = "Artist",
                            Album = "Album",
                            TrackUri = query,
                            Provider = "stub",
                        },
                    ],
                    MusicProviderFailureReason.None
                )
            );

        public Task<TrackInfo?> GetCurrentTrackAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<TrackInfo?>(null);

        public Task<bool> AddToQueueAsync(
            Guid broadcasterId,
            string trackUri,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);

        public Task<IReadOnlyList<TrackInfo>> GetQueueAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<TrackInfo>>([]);

        public Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PreviousAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetVolumeAsync(
            Guid broadcasterId,
            int volumePercent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SeekAsync(
            Guid broadcasterId,
            int positionSeconds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetShuffleAsync(
            Guid broadcasterId,
            bool enabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetRepeatAsync(
            Guid broadcasterId,
            MusicRepeatMode mode,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task TransferPlaybackAsync(
            Guid broadcasterId,
            string deviceId,
            bool play,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string?> GetEmbeddedPlaybackTokenAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
