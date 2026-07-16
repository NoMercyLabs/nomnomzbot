// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NomNomzBot.Infrastructure.Supporters.Sockets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the live-socket ingress runner (supporter-events.md §0 D3) by its consequences against the REAL
/// ingest path: a reconcile starts one runner for an enabled ws connection with a sealed key, its frames
/// translate through the provider profile and persist as <c>tip</c> <see cref="SupporterEvent"/>s (a
/// replayed frame after a reconnect dedups); a disabled connection starts nothing; a missing key marks the
/// connection <c>error</c> without a stream; disabling later stops the runner; and a denied run-once lease
/// keeps this instance socket-free.
/// </summary>
public sealed class SupporterSocketHostedServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-4444-7000-8000-000000000001");

    private const string TipFrame = """
        { "type": "campaigntip.notify", "payload": { "campaignTip": { "id": "t-1", "displayName": "Someone", "grossAmountInCents": 500, "message": "hi" } } }
        """;

    private static async Task<(
        SupporterSocketHostedService Service,
        SupporterTestDbContext Db,
        QueueFrameSource Frames
    )> BuildAsync(bool enabled = true, string? secret = "pally-key", IRunOnceGuard? guard = null)
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.SupporterConnections.Add(
            new SupporterConnection
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                SourceKey = "pally",
                ConnectionMode = "ws",
                IsEnabled = enabled,
                Status = "idle",
                AuthSecretCipher = secret is null ? null : $"sealed:{secret}",
            }
        );
        await db.SaveChangesAsync();

        SupporterIngestService ingest = new(
            db,
            [new PallySupporterSource()],
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<SupporterIngestService>.Instance
        );

        ServiceCollection services = new();
        services.AddSingleton<IRunOnceGuard>(guard ?? new NoOpRunOnceGuard());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITokenProtector>(new PrefixProtector());
        services.AddSingleton<ISupporterIngestService>(ingest);
        ServiceProvider provider = services.BuildServiceProvider();

        QueueFrameSource frames = new();
        SupporterSocketHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            frames,
            [new PallySocketProfile()],
            TimeProvider.System,
            NullLogger<SupporterSocketHostedService>.Instance
        );
        return (service, db, frames);
    }

    [Fact]
    public async Task Reconcile_StartsARunner_WhoseFramesPersistAsTips_AndAReplayDedups()
    {
        (SupporterSocketHostedService service, SupporterTestDbContext db, QueueFrameSource frames) =
            await BuildAsync();
        frames.Enqueue(TipFrame);
        frames.Enqueue("pong"); // keepalive echo — never an event
        frames.Enqueue(TipFrame); // the same tip replayed (e.g. after a reconnect) — dedups

        await service.ReconcileOnceAsync(CancellationToken.None);
        // Every queued frame consumed (and its ingest awaited) by the runner.
        await frames.Drained.WaitAsync(TimeSpan.FromSeconds(10));

        frames.ConnectedSecrets.Should().HaveCount(1);
        frames
            .ConnectedSecrets[0]
            .Should()
            .Be("pally-key", "the runner hands the transport the UNSEALED key");

        List<SupporterEvent> events = await db.SupporterEvents.ToListAsync();
        events.Should().HaveCount(1, "the replayed frame dedups on the tip id");
        events[0].Kind.Should().Be("tip");
        events[0].SourceKey.Should().Be("pally");
        events[0].AmountMinor.Should().Be(500);
        events[0].ProviderTransactionId.Should().Be("t-1");

        await service.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_DisabledConnection_StartsNothing()
    {
        (SupporterSocketHostedService service, _, QueueFrameSource frames) = await BuildAsync(
            enabled: false
        );

        await service.ReconcileOnceAsync(CancellationToken.None);

        frames.ConnectedSecrets.Should().BeEmpty();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_MissingKey_MarksTheConnectionError_WithoutAStream()
    {
        (SupporterSocketHostedService service, SupporterTestDbContext db, QueueFrameSource frames) =
            await BuildAsync(secret: null);

        await service.ReconcileOnceAsync(CancellationToken.None);

        frames.ConnectedSecrets.Should().BeEmpty();
        (await db.SupporterConnections.SingleAsync()).Status.Should().Be("error");
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_DisablingLater_StopsTheRunner()
    {
        (SupporterSocketHostedService service, SupporterTestDbContext db, QueueFrameSource frames) =
            await BuildAsync();

        await service.ReconcileOnceAsync(CancellationToken.None);
        await frames.FirstConnected.WaitAsync(TimeSpan.FromSeconds(10)); // runner startup is async
        frames.ConnectedSecrets.Should().HaveCount(1);

        SupporterConnection connection = await db.SupporterConnections.SingleAsync();
        connection.IsEnabled = false;
        await db.SaveChangesAsync();

        await service.ReconcileOnceAsync(CancellationToken.None);

        // The runner's stream was cancelled — the frame source observed the disconnect.
        frames.ActiveStreams.Should().Be(0);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_DeniedLease_KeepsThisInstanceSocketFree()
    {
        IRunOnceGuard denied = Substitute.For<IRunOnceGuard>();
        denied
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((IAsyncDisposable?)null);
        (SupporterSocketHostedService service, _, QueueFrameSource frames) = await BuildAsync(
            guard: denied
        );

        await service.ReconcileOnceAsync(CancellationToken.None);

        frames.ConnectedSecrets.Should().BeEmpty();
        await service.DisposeAsync();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    /// <summary>
    /// A scripted transport: yields the queued frames, then waits (an idle open socket) until cancelled.
    /// <see cref="Drained"/> completes when every queued frame has been consumed, giving tests a race-free
    /// point to assert the ingested consequences.
    /// </summary>
    private sealed class QueueFrameSource : ISocketFrameSource
    {
        private readonly Queue<string> _frames = new();
        private readonly TaskCompletionSource _drained = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _firstConnected = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _active;

        public List<string> ConnectedSecrets { get; } = [];
        public int ActiveStreams => Volatile.Read(ref _active);
        public Task Drained => _drained.Task;
        public Task FirstConnected => _firstConnected.Task;

        public void Enqueue(string frame) => _frames.Enqueue(frame);

        public async IAsyncEnumerable<string> ConnectAndReceiveAsync(
            ISupporterSocketProfile profile,
            string secret,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            ConnectedSecrets.Add(secret);
            _firstConnected.TrySetResult();
            Interlocked.Increment(ref _active);
            try
            {
                while (_frames.Count > 0)
                {
                    string frame = _frames.Dequeue();
                    yield return frame;
                }
                _drained.TrySetResult();
                // An open, idle socket: stay connected until the runner is cancelled.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    /// <summary>Transparent AEAD stand-in (<c>sealed:&lt;plaintext&gt;</c>) — the crypto is proven elsewhere.</summary>
    private sealed class PrefixProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"sealed:{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? sealedEnvelope,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                sealedEnvelope is not null && sealedEnvelope.StartsWith("sealed:")
                    ? sealedEnvelope["sealed:".Length..]
                    : null
            );
    }
}
