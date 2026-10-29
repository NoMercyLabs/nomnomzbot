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
/// Proves the idle pool that keeps the Edge voice connections open between clips: a connection that is still
/// fresh and open is handed out again (no new handshake), one that is too old or closed is thrown away, and the
/// pool never holds more than its limit.
/// </summary>
public sealed class IdleConnectionPoolTests
{
    private sealed class Conn : IDisposable
    {
        public bool Open { get; set; } = true;
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static readonly TimeSpan MaxIdle = TimeSpan.FromSeconds(20);

    private static IdleConnectionPool<Conn> Pool(FakeTimeProvider time, int capacity = 2) =>
        new(time, capacity, MaxIdle, c => c.Open);

    [Fact]
    public void A_connection_that_was_given_back_is_handed_out_again()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time);
        Conn conn = new();

        pool.Return(conn);

        pool.TryRent(out Conn? rented).Should().BeTrue();
        rented.Should().BeSameAs(conn);
        conn.Disposed.Should().BeFalse();
        pool.TryRent(out _).Should().BeFalse("it is in use now, not idle");
    }

    [Fact]
    public void A_connection_idle_for_too_long_is_thrown_away_not_reused()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time);
        Conn conn = new();
        pool.Return(conn);

        time.Advance(MaxIdle + TimeSpan.FromSeconds(1));

        pool.TryRent(out _).Should().BeFalse();
        conn.Disposed.Should().BeTrue();
    }

    [Fact]
    public void A_connection_that_closed_while_idle_is_thrown_away_not_reused()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time);
        Conn conn = new();
        pool.Return(conn);

        conn.Open = false;

        pool.TryRent(out _).Should().BeFalse();
        conn.Disposed.Should().BeTrue();
    }

    [Fact]
    public void The_most_recently_used_connection_goes_out_first_and_a_stale_one_below_it_is_cleared()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time, capacity: 3);
        Conn old = new();
        Conn fresh = new();
        pool.Return(old);
        time.Advance(MaxIdle);
        pool.Return(fresh);
        time.Advance(TimeSpan.FromSeconds(5));

        pool.TryRent(out Conn? first).Should().BeTrue();
        first.Should().BeSameAs(fresh);
        pool.TryRent(out _).Should().BeFalse("the other one idled past the limit");
        old.Disposed.Should().BeTrue();
    }

    [Fact]
    public void A_connection_given_back_over_the_limit_is_disposed()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time, capacity: 2);
        Conn a = new();
        Conn b = new();
        Conn c = new();

        pool.Return(a);
        pool.Return(b);
        pool.Return(c);

        c.Disposed.Should().BeTrue();
        a.Disposed.Should().BeFalse();
        b.Disposed.Should().BeFalse();
    }

    [Fact]
    public void A_connection_that_is_already_closed_when_given_back_is_disposed_not_pooled()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time);
        Conn conn = new() { Open = false };

        pool.Return(conn);

        conn.Disposed.Should().BeTrue();
        pool.TryRent(out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_disposes_every_idle_connection()
    {
        FakeTimeProvider time = new();
        IdleConnectionPool<Conn> pool = Pool(time);
        Conn a = new();
        Conn b = new();
        pool.Return(a);
        pool.Return(b);

        pool.Clear();

        a.Disposed.Should().BeTrue();
        b.Disposed.Should().BeTrue();
        pool.TryRent(out _).Should().BeFalse();
    }
}
