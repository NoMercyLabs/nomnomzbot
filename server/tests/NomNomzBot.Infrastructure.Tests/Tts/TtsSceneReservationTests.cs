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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves <see cref="TtsSceneReservation"/>'s real failure modes: a waiter is released the moment the
/// holder releases (not just on a timeout), a mismatched/stale token never releases someone else's newer
/// reservation, and an un-released reservation stops blocking once it expires.
/// </summary>
public sealed class TtsSceneReservationTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-0000-7000-8000-00000000c0de");
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WaitForClearAsync_NoReservation_ReturnsImmediately()
    {
        TtsSceneReservation reservation = new();

        await reservation
            .WaitForClearAsync(Channel, callerToken: null)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForClearAsync_CallerHoldsTheOnlyActiveReservation_ReturnsImmediately()
    {
        TtsSceneReservation reservation = new();
        string token = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(1));

        await reservation
            .WaitForClearAsync(Channel, callerToken: token)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForClearAsync_ADifferentCaller_WaitsUntilReleased_NotJustUntilExpiry()
    {
        TtsSceneReservation reservation = new();
        string token = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(10));

        Task waiter = reservation.WaitForClearAsync(Channel, callerToken: null);
        await Task.Delay(50);
        waiter.IsCompleted.Should().BeFalse();

        await reservation.ReleaseAsync(Channel, token);

        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleaseAsync_WithAStaleToken_NeverClearsANewerReservation()
    {
        TtsSceneReservation reservation = new();
        string staleToken = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(10));
        string currentToken = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(10));

        await reservation.ReleaseAsync(Channel, staleToken);

        // The current holder's reservation must still be in force — a waiter carrying neither token waits.
        Task waiter = reservation.WaitForClearAsync(Channel, callerToken: null);
        await Task.Delay(50);
        waiter.IsCompleted.Should().BeFalse();

        await reservation.ReleaseAsync(Channel, currentToken);
        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForClearAsync_ReservationNeverReleased_ClearsOnceItExpires()
    {
        FakeTimeProvider clock = new(T0);
        TtsSceneReservation reservation = new(clock);
        await reservation.ReserveAsync(Channel, TimeSpan.FromSeconds(30));

        Task waiter = reservation.WaitForClearAsync(Channel, callerToken: null);
        clock.Advance(TimeSpan.FromSeconds(30));

        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReserveAsync_ANewReservation_SupersedesAnEarlierOneEvenIfStillActive()
    {
        TtsSceneReservation reservation = new();
        string first = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(10));

        string second = await reservation.ReserveAsync(Channel, TimeSpan.FromMinutes(10));

        second.Should().NotBe(first);
        // The first token no longer owns anything to release.
        await reservation.ReleaseAsync(Channel, first);
        Task waiter = reservation.WaitForClearAsync(Channel, callerToken: null);
        await Task.Delay(50);
        waiter.IsCompleted.Should().BeFalse("the second reservation is still active");

        await reservation.ReleaseAsync(Channel, second);
        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
