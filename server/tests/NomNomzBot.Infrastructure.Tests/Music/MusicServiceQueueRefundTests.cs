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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S067b — proves <see cref="MusicService.RemoveFromQueueAsync"/> and
/// <see cref="MusicService.BanQueuedTrackAsync"/> refund a removed entry's <c>Cost</c> to its
/// <c>RequesterUserId</c> via the real <see cref="ICurrencyAccountService.PostLedgerEntryAsync"/> ledger
/// call — and, just as importantly, that a free entry (the only kind any admission path produces today)
/// never triggers a refund call at all, so this mechanism can never fabricate a refund for a request
/// nothing charged.
/// </summary>
public sealed class MusicServiceQueueRefundTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f0004");
    private static readonly Guid ViewerId = Guid.Parse("0192a000-0000-7000-8000-0000000f00aa");

    [Fact]
    public async Task RemoveFromQueue_of_a_paid_entry_refunds_the_requester_the_exact_cost()
    {
        (MusicService sut, ICurrencyAccountService accounts, ISongRequestQueueStore store) =
            Build();
        SeedPaidEntry(store, "spotify:track:paid1", cost: 150, ViewerId, "viewer1");

        bool removed = await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0);

        removed.Should().BeTrue();
        await accounts
            .Received(1)
            .PostLedgerEntryAsync(
                ChannelId,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.ViewerUserId == ViewerId
                    && c.Amount == 150
                    && c.EntryType == nameof(CurrencyEntryType.RefundSongRequest)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoveFromQueue_of_a_free_entry_never_calls_the_refund_path()
    {
        (MusicService sut, ICurrencyAccountService accounts, ISongRequestQueueStore store) =
            Build();
        SeedPaidEntry(store, "spotify:track:free1", cost: 0, requesterUserId: null, "viewer1");

        bool removed = await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0);

        removed.Should().BeTrue();
        await accounts
            .DidNotReceive()
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BanQueuedTrack_of_a_paid_entry_refunds_the_requester_the_exact_cost()
    {
        (MusicService sut, ICurrencyAccountService accounts, ISongRequestQueueStore store) =
            Build();
        SeedPaidEntry(store, "spotify:track:paid2", cost: 75, ViewerId, "viewer1");

        Result<BlockedTrackDto> result = await sut.BanQueuedTrackAsync(ChannelId.ToString(), 0);

        result.IsSuccess.Should().BeTrue();
        await accounts
            .Received(1)
            .PostLedgerEntryAsync(
                ChannelId,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.ViewerUserId == ViewerId
                    && c.Amount == 75
                    && c.EntryType == nameof(CurrencyEntryType.RefundSongRequest)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BanQueuedTrack_of_a_free_entry_never_calls_the_refund_path()
    {
        (MusicService sut, ICurrencyAccountService accounts, ISongRequestQueueStore store) =
            Build();
        SeedPaidEntry(store, "spotify:track:free2", cost: 0, requesterUserId: null, "viewer1");

        Result<BlockedTrackDto> result = await sut.BanQueuedTrackAsync(ChannelId.ToString(), 0);

        result.IsSuccess.Should().BeTrue();
        await accounts
            .DidNotReceive()
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetQueueAsync_surfaces_the_real_cost_of_a_paid_entry_and_zero_for_a_free_one()
    {
        (MusicService sut, _, ISongRequestQueueStore store) = Build();
        SeedPaidEntry(store, "spotify:track:paid3", cost: 200, ViewerId, "viewer1");
        SeedPaidEntry(store, "spotify:track:free3", cost: 0, requesterUserId: null, "viewer2");

        NomNomzBot.Application.Music.Services.MusicQueue queue = await sut.GetQueueAsync(
            ChannelId.ToString()
        );

        queue.Queue.Should().HaveCount(2);
        queue.Queue.Should().ContainSingle(i => i.RequestedBy == "viewer1" && i.Cost == 200);
        queue.Queue.Should().ContainSingle(i => i.RequestedBy == "viewer2" && i.Cost == 0);
    }

    /// <summary>Seeds a queue entry directly into the store at the given cost/requester — the only way to
    /// get a paid entry into the queue today, since no admission path (<c>RequestTrackAsync</c>/
    /// <c>AddToQueueAsync</c>) charges for a song request yet (S067b ground truth). This proves the
    /// refund MECHANISM independently of that still-missing charge.</summary>
    private static void SeedPaidEntry(
        ISongRequestQueueStore store,
        string trackUri,
        int cost,
        Guid? requesterUserId,
        string requestedBy
    ) =>
        store
            .GetOrCreate(ChannelId.ToString())
            .Enqueue(
                requestedBy,
                new SongRequestEntry(
                    trackUri,
                    "Some Song",
                    "Some Artist",
                    ImageUrl: null,
                    DurationMs: 200000,
                    requestedBy,
                    cost,
                    requesterUserId
                )
            );

    private static (
        MusicService Sut,
        ICurrencyAccountService Accounts,
        ISongRequestQueueStore Store
    ) Build()
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

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(new NoOpSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        ISongRequestQueueStore store = new SongRequestQueueStore();
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new CurrencyLedgerEntryDto(
                        Id: 0,
                        TenantPosition: 0,
                        AccountId: Guid.Empty,
                        ViewerUserId: ViewerId,
                        Amount: 0,
                        BalanceAfter: 0,
                        EntryType: nameof(CurrencyEntryType.RefundSongRequest),
                        SourceType: nameof(CurrencyLedgerSourceType.SongRequest),
                        SourceId: null,
                        RelatedEntryId: null,
                        EventId: null,
                        Reason: null,
                        ActorUserId: null,
                        CreatedAt: DateTime.UtcNow
                    )
                )
            );

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            accounts,
            new NowPlayingCache()
        );
        return (sut, accounts, store);
    }

    /// <summary>Every call returns 204 — these tests never search/resolve a track, they seed the queue
    /// directly, so the provider only needs to not blow up if MusicService happens to touch it.</summary>
    private sealed class NoOpSpotifyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
