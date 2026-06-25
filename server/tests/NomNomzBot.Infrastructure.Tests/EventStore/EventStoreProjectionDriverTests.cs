// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Proves the projection driver's dispatch contract (event-store.md §3.3): each pass advances a GLOBAL projection
/// once over the platform stream and a TENANT projection once per channel in the DB (not the in-memory registry,
/// so a channel whose bot is offline still catches up), and a pass is skipped entirely when the run-once lease is
/// held by another instance (the SaaS single-driver guard).
/// </summary>
public sealed class EventStoreProjectionDriverTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");

    [Fact]
    public async Task DriveAsync_drives_global_once_and_tenant_once_per_db_channel()
    {
        IProjectionRunner runner = Substitute.For<IProjectionRunner>();
        runner
            .RunOnceAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(0L));

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(ChannelA));
        db.Channels.Add(Channel(ChannelB));
        await db.SaveChangesAsync();

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => runner)
            .AddScoped<IApplicationDbContext>(_ => db)
            .AddScoped<IProjection>(_ => new StubProjection("global-events", isGlobal: true))
            .AddScoped<IProjection>(_ => new StubProjection("viewer-profile", isGlobal: false))
            .BuildServiceProvider();

        EventStoreProjectionDriver driver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new GrantingRunOnceGuard(),
            NullLogger<EventStoreProjectionDriver>.Instance
        );

        await driver.DriveAsync(CancellationToken.None);

        // The global projection runs once over the platform stream (null scope) — never per channel.
        await runner.Received(1).RunOnceAsync("global-events", null, Arg.Any<CancellationToken>());
        await runner
            .DidNotReceive()
            .RunOnceAsync("global-events", ChannelA, Arg.Any<CancellationToken>());
        // The tenant projection runs once for EACH channel in the DB — this is what catches up the read models.
        await runner
            .Received(1)
            .RunOnceAsync("viewer-profile", ChannelA, Arg.Any<CancellationToken>());
        await runner
            .Received(1)
            .RunOnceAsync("viewer-profile", ChannelB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DriveAsync_skips_the_pass_when_the_run_once_lease_is_not_granted()
    {
        IProjectionRunner runner = Substitute.For<IProjectionRunner>();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => runner)
            .BuildServiceProvider();

        EventStoreProjectionDriver driver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DenyingRunOnceGuard(),
            NullLogger<EventStoreProjectionDriver>.Instance
        );

        await driver.DriveAsync(CancellationToken.None);

        // Another instance holds the lease ⇒ no projection is touched, no DB read even happens this pass.
        await runner
            .DidNotReceive()
            .RunOnceAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ── doubles ──────────────────────────────────────────────────────────────────

    private static Channel Channel(Guid id) =>
        new()
        {
            Id = id,
            OwnerUserId = id,
            TwitchChannelId = id.ToString(),
            Name = "ch",
            NameNormalized = "ch",
        };

    private sealed class StubProjection(string name, bool isGlobal) : IProjection
    {
        public string Name => name;
        public bool IsGlobal => isGlobal;
        public IReadOnlySet<string> SubscribedEventTypes => new HashSet<string>();

        public Task<Result> ApplyAsync(
            EventRecord @event,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success());

        public Task<Result> ResetAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success());
    }

    private sealed class GrantingRunOnceGuard : IRunOnceGuard
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable?>(new NoOpLease());

        private sealed class NoOpLease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class DenyingRunOnceGuard : IRunOnceGuard
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable?>(null);
    }
}
