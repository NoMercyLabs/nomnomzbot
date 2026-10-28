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

using Microsoft.Extensions.Time.Testing;

namespace NomNomzBot.Infrastructure.Tests.Common;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers created on it. A background loop creates its
/// delay timer on a real thread-pool turn, AFTER its tick returns. A test that advances the clock before
/// that timer exists moves the clock past a timer that is not there yet, and the loop then waits a full
/// interval from the new time. Waiting for the timer first makes the advance deterministic.
/// </summary>
public sealed class TimerCountingTimeProvider : FakeTimeProvider
{
    private int _timersCreated;

    public TimerCountingTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime) { }

    public int TimersCreated => Volatile.Read(ref _timersCreated);

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        // Count after the base call: the base registers the timer with the clock before it returns.
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timersCreated);
        return timer;
    }

    /// <summary>Waits until at least <paramref name="count"/> timers exist; throws on timeout.</summary>
    public Task WaitForTimersAsync(int count, TimeSpan? timeout = null) =>
        TestWait.UntilAsync(
            () => TimersCreated >= count,
            () => $"the background loop to create timer #{count} (created: {TimersCreated})",
            timeout
        );
}
