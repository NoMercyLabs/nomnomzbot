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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Rewards.Jobs;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// S037 — a throwing tick used to skip its inter-tick delay (the delay sat INSIDE the try, so an exception
/// jumped straight past it), spinning the loop hot against whatever just failed. The delay now runs whether
/// the tick threw or not: a second attempt must NOT happen until a full <c>TickInterval</c> (2s) has elapsed
/// on the (fake, controllable) clock.
/// </summary>
public sealed class RedemptionTimerExpiryServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenTickThrows_StillWaitsTheFullIntervalBeforeRetrying()
    {
        FakeTimeProvider clock = new(Start);
        ThrowingScopeFactory scopeFactory = new();

        RedemptionTimerExpiryService sut = new(
            scopeFactory,
            clock,
            NullLogger<RedemptionTimerExpiryService>.Instance
        );

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);
        try
        {
            await WaitUntilAsync(() => scopeFactory.CallCount >= 1);
            scopeFactory.CallCount.Should().Be(1);

            // Advancing LESS than the 2s interval must not release the delay — no second tick yet.
            clock.Advance(TimeSpan.FromMilliseconds(1900));
            await Task.Delay(50, CancellationToken.None);
            scopeFactory.CallCount.Should().Be(1, "the failing tick's delay has not elapsed yet");

            // Crossing the interval releases the delay and the loop retries exactly once more.
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await WaitUntilAsync(() => scopeFactory.CallCount >= 2);
            scopeFactory.CallCount.Should().Be(2);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    /// <summary>A scope factory that always fails to create a scope — simulates a throwing tick.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CallCount++;
            throw new InvalidOperationException("scope boom");
        }
    }
}
