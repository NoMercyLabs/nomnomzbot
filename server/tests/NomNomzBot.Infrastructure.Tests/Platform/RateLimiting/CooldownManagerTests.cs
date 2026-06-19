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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Platform.RateLimiting;

namespace NomNomzBot.Infrastructure.Tests.Platform.RateLimiting;

public class CooldownManagerTests
{
    // A fixed, controllable clock. CooldownManager reads only through the injected
    // TimeProvider (single-clock convention, platform-conventions §3.11), so advancing
    // this fake clock — never a real-clock Thread.Sleep — drives every expiry path
    // deterministically.
    private static (CooldownManager Manager, FakeTimeProvider Clock) Create()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (new CooldownManager(clock), clock);
    }

    // ─── IsOnCooldown ─────────────────────────────────────────────────────────

    [Fact]
    public void IsOnCooldown_NoCooldownSet_ReturnsFalse()
    {
        (CooldownManager mgr, _) = Create();
        mgr.IsOnCooldown("chan", "!so").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_AfterSet_ReturnsTrue()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!so", TimeSpan.FromSeconds(30));

        mgr.IsOnCooldown("chan", "!so").Should().BeTrue();
    }

    [Fact]
    public void IsOnCooldown_ObservesFakedClock_ExpiresOnlyAfterDurationElapses()
    {
        (CooldownManager mgr, FakeTimeProvider clock) = Create();
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(30));

        // Just before the duration elapses on the FAKED clock — still on cooldown.
        clock.Advance(TimeSpan.FromSeconds(29));
        mgr.IsOnCooldown("chan", "!cmd").Should().BeTrue();

        // Cross the exact expiry boundary (expiresAt == now is treated as expired) — released.
        clock.Advance(TimeSpan.FromSeconds(1));
        mgr.IsOnCooldown("chan", "!cmd").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_GlobalCooldown_DoesNotAffectPerUserKey()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!so", TimeSpan.FromSeconds(60)); // global

        // Per-user key should not be on cooldown
        mgr.IsOnCooldown("chan", "!so", "user1").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_PerUserCooldown_DoesNotAffectOtherUser()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!so", TimeSpan.FromSeconds(60), "user1");

        mgr.IsOnCooldown("chan", "!so", "user2").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_DifferentChannels_Independent()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan1", "!cmd", TimeSpan.FromSeconds(60));

        mgr.IsOnCooldown("chan2", "!cmd").Should().BeFalse();
    }

    // ─── GetRemainingCooldown ────────────────────────────────────────────────

    [Fact]
    public void GetRemainingCooldown_NoCooldown_ReturnsNull()
    {
        (CooldownManager mgr, _) = Create();
        mgr.GetRemainingCooldown("chan", "!cmd").Should().BeNull();
    }

    [Fact]
    public void GetRemainingCooldown_ActiveCooldown_ReturnsPositive()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(60));

        TimeSpan? remaining = mgr.GetRemainingCooldown("chan", "!cmd");
        remaining.Should().NotBeNull();
        remaining!.Value.Should().BePositive();
        remaining.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void GetRemainingCooldown_CountsDownAgainstFakedClock_ThenReturnsNull()
    {
        (CooldownManager mgr, FakeTimeProvider clock) = Create();
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(60));

        // Remaining is computed against the injected clock: after advancing 20s of FAKE
        // time, exactly 40s must remain — to the tick, with no real-clock jitter.
        clock.Advance(TimeSpan.FromSeconds(20));
        mgr.GetRemainingCooldown("chan", "!cmd").Should().Be(TimeSpan.FromSeconds(40));

        // Advance past expiry on the faked clock — the entry is gone.
        clock.Advance(TimeSpan.FromSeconds(40));
        mgr.GetRemainingCooldown("chan", "!cmd").Should().BeNull();
    }

    // ─── SetCooldown ──────────────────────────────────────────────────────────

    [Fact]
    public void SetCooldown_Overwrite_UpdatesExpiry()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(10));
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(60)); // overwrite

        TimeSpan? remaining = mgr.GetRemainingCooldown("chan", "!cmd");
        remaining.Should().NotBeNull();
        remaining!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(30));
    }

    // ─── ClearCooldown ────────────────────────────────────────────────────────

    [Fact]
    public void ClearCooldown_ExistingCooldown_RemovesIt()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!cmd", TimeSpan.FromSeconds(60));
        mgr.ClearCooldown("chan", "!cmd");

        mgr.IsOnCooldown("chan", "!cmd").Should().BeFalse();
    }

    [Fact]
    public void ClearCooldown_NonExistentKey_DoesNotThrow()
    {
        (CooldownManager mgr, _) = Create();
        Action act = () => mgr.ClearCooldown("chan", "!nonexistent");

        act.Should().NotThrow();
    }

    [Fact]
    public void ClearCooldown_PerUser_OnlyClearsUserKey()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!so", TimeSpan.FromSeconds(60)); // global
        mgr.SetCooldown("chan", "!so", TimeSpan.FromSeconds(60), "user1"); // per-user

        mgr.ClearCooldown("chan", "!so", "user1");

        mgr.IsOnCooldown("chan", "!so", "user1").Should().BeFalse();
        mgr.IsOnCooldown("chan", "!so").Should().BeTrue(); // global untouched
    }

    // ─── ClearAllCooldowns ────────────────────────────────────────────────────

    [Fact]
    public void ClearAllCooldowns_RemovesAllForChannel()
    {
        (CooldownManager mgr, _) = Create();
        mgr.SetCooldown("chan", "!cmd1", TimeSpan.FromSeconds(60));
        mgr.SetCooldown("chan", "!cmd2", TimeSpan.FromSeconds(60));
        mgr.SetCooldown("chan", "!cmd3", TimeSpan.FromSeconds(60), "user1");
        mgr.SetCooldown("other-chan", "!cmd1", TimeSpan.FromSeconds(60));

        mgr.ClearAllCooldowns("chan");

        mgr.IsOnCooldown("chan", "!cmd1").Should().BeFalse();
        mgr.IsOnCooldown("chan", "!cmd2").Should().BeFalse();
        mgr.IsOnCooldown("chan", "!cmd3", "user1").Should().BeFalse();
        mgr.IsOnCooldown("other-chan", "!cmd1").Should().BeTrue(); // untouched
    }

    [Fact]
    public void ClearAllCooldowns_EmptyChannel_DoesNotThrow()
    {
        (CooldownManager mgr, _) = Create();
        Action act = () => mgr.ClearAllCooldowns("nonexistent-channel");

        act.Should().NotThrow();
    }

    // ─── Concurrency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CooldownManager_ConcurrentAccess_DoesNotThrow()
    {
        (CooldownManager mgr, _) = Create();
        IEnumerable<Task> tasks = Enumerable
            .Range(0, 50)
            .Select(i =>
                Task.Run(() =>
                {
                    mgr.SetCooldown("chan", $"!cmd{i % 5}", TimeSpan.FromSeconds(10), $"user{i}");
                    mgr.IsOnCooldown("chan", $"!cmd{i % 5}", $"user{i}");
                    mgr.GetRemainingCooldown("chan", $"!cmd{i % 5}", $"user{i}");
                })
            );

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }
}
