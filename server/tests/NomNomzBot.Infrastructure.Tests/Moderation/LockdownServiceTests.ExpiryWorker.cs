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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation.Lockdown;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The expiry worker drives <see cref="ILockdownService.RestoreDueAsync"/> on a 15 second tick, against the same
/// stateful fake of the room as the service tests. The fake clock moves one interval at a time and each step
/// waits for the tick it caused (the run-once lease is released when the tick ends), so nothing sleeps.
/// </summary>
public partial class LockdownServiceTests
{
    private static readonly TimeSpan WorkerTick = LockdownExpiryWorker.TickInterval;
    private static readonly TimeSpan TickWaitLimit = TimeSpan.FromSeconds(30);

    private int _restoreCalls;
    private Func<int, bool> _restoreThrows = _ => false;

    private sealed class TickSignalGuard : IRunOnceGuard
    {
        private readonly SemaphoreSlim _tickEnded = new(0);

        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable?>(new Lease(_tickEnded));

        public Task WaitForTickEndAsync() => _tickEnded.WaitAsync(TickWaitLimit);

        private sealed class Lease(SemaphoreSlim tickEnded) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                tickEnded.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>The real service, except that RestoreDueAsync can be made to throw on chosen calls.</summary>
    private sealed class FlakyLockdownService(
        LockdownService inner,
        Func<int> nextCallNumber,
        Func<int, bool> throws
    ) : ILockdownService
    {
        public Task<Result<LockdownWindowStatus>> EngageAsync(
            Guid broadcasterId,
            string platform,
            string trigger,
            IReadOnlyCollection<LockdownControl> requested,
            CancellationToken ct = default
        ) => inner.EngageAsync(broadcasterId, platform, trigger, requested, ct);

        public Task<Result<LockdownWindowStatus>> EndAsync(
            Guid broadcasterId,
            string platform,
            CancellationToken ct = default
        ) => inner.EndAsync(broadcasterId, platform, ct);

        public Task<IReadOnlyList<LockdownWindowStatus>> GetActiveAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => inner.GetActiveAsync(broadcasterId, ct);

        public Task<int> RestoreDueAsync(CancellationToken ct = default)
        {
            int call = nextCallNumber();
            if (throws(call))
                throw new InvalidOperationException("restore sweep failed");
            return inner.RestoreDueAsync(ct);
        }
    }

    private (LockdownExpiryWorker Worker, TickSignalGuard Guard) NewExpiryWorker()
    {
        TickSignalGuard guard = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IRunOnceGuard>(guard)
            .AddScoped(_ => NewDbContext())
            .AddScoped<ILockdownService>(sp =>
            {
                ISpamDefenseService spam = Substitute.For<ISpamDefenseService>();
                spam.GetSettingsAsync(Channel, Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromResult(_policy));
                LockdownService real = new(
                    sp.GetRequiredService<AppDbContext>(),
                    spam,
                    [new TwitchLockdownAdapter(_chat, _moderation)],
                    _time,
                    NullLogger<LockdownService>.Instance
                );
                return new FlakyLockdownService(
                    real,
                    () => Interlocked.Increment(ref _restoreCalls),
                    call => _restoreThrows(call)
                );
            })
            .BuildServiceProvider();

        LockdownExpiryWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _time,
            NullLogger<LockdownExpiryWorker>.Instance
        );
        return (worker, guard);
    }

    /// <summary>Starts the worker and waits for the sweep it runs straight away.</summary>
    private static async Task StartAsync(LockdownExpiryWorker worker, TickSignalGuard guard)
    {
        await worker.StartAsync(CancellationToken.None);
        await guard.WaitForTickEndAsync();
    }

    /// <summary>Moves the fake clock one worker interval and waits for the sweep that causes.</summary>
    private async Task NextTickAsync(TickSignalGuard guard)
    {
        _time.Advance(WorkerTick);
        await guard.WaitForTickEndAsync();
    }

    [Fact]
    public async Task The_worker_restores_an_expired_window_on_a_tick_and_not_before()
    {
        _policy = new SpamDefenseSettings { LockdownMinutes = 1 };
        await EngageAsync("twitch", LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        Windows().Single().ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(1));
        (LockdownExpiryWorker worker, TickSignalGuard guard) = NewExpiryWorker();

        await StartAsync(worker, guard);
        for (int i = 0; i < 3; i++)
            await NextTickAsync(guard);

        Windows()
            .Single()
            .RestoredAt.Should()
            .BeNull("the window runs until 60 s and only 45 s have passed");
        _room.FollowerMode.Should().BeTrue();
        _shieldOn.Should().BeTrue();

        await NextTickAsync(guard);

        LockdownWindowRecord window = Windows().Single();
        window.RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(1));
        ControlsOf(window.RestorationFailedControlsJson).Should().BeEmpty();
        _room.FollowerMode.Should().BeFalse();
        _shieldOn.Should().BeFalse();
        AssertNoActionAgainstAPerson();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_restore_that_fails_on_one_tick_is_retried_on_the_next_and_completes()
    {
        _policy = new SpamDefenseSettings { LockdownMinutes = 1 };
        await EngageAsync("twitch", LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        _shieldUpdateFails = wanted => !wanted;
        (LockdownExpiryWorker worker, TickSignalGuard guard) = NewExpiryWorker();

        await StartAsync(worker, guard);
        for (int i = 0; i < 4; i++)
            await NextTickAsync(guard);

        LockdownWindowRecord window = Windows().Single();
        window.RestoredAt.Should().BeNull("shield mode could not be switched off");
        ControlsOf(window.RestorationFailedControlsJson).Should().Equal(LockdownControl.ShieldMode);
        _room.FollowerMode.Should().BeFalse();
        _shieldOn.Should().BeTrue();

        _shieldUpdateFails = _ => false;
        await NextTickAsync(guard);

        window = Windows().Single();
        window.RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(1).AddSeconds(15));
        ControlsOf(window.RestorationFailedControlsJson).Should().BeEmpty();
        _shieldOn.Should().BeFalse();
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(FollowerMode: false),
                Arg.Any<CancellationToken>()
            );
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_sweep_that_throws_does_not_stop_the_next_tick()
    {
        _policy = new SpamDefenseSettings { LockdownMinutes = 1 };
        await EngageAsync("twitch", LockdownControl.FollowersOnly);
        _restoreThrows = call => call == 5;
        (LockdownExpiryWorker worker, TickSignalGuard guard) = NewExpiryWorker();

        await StartAsync(worker, guard);
        for (int i = 0; i < 3; i++)
            await NextTickAsync(guard);

        await NextTickAsync(guard);
        _restoreCalls.Should().Be(5);
        Windows().Single().RestoredAt.Should().BeNull("the sweep that was due at 60 s threw");
        _room.FollowerMode.Should().BeTrue();

        await NextTickAsync(guard);

        _restoreCalls.Should().Be(6);
        Windows().Single().RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(1).AddSeconds(15));
        _room.FollowerMode.Should().BeFalse();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void The_host_scan_registers_the_worker_as_a_hosted_service()
    {
        ServiceCollection services = new();

        services.AddHostedWorkers(typeof(LockdownExpiryWorker).Assembly);

        services
            .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
            .Should()
            .NotBeEmpty();
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(LockdownExpiryWorker)
                && d.Lifetime == ServiceLifetime.Singleton
            );
    }
}
