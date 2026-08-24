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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.HealthChecks;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Proves the S116 debounce behavior: a normal EventSub reconnect (Twitch's ~5 minute
/// <c>session_reconnect</c> cycle — README "Known Issues") must NOT flap <c>/health/ready</c>, but a
/// disconnect that outlives <see cref="EventSubDisconnectTracker.GracePeriod"/> must degrade it (which maps
/// to 503 via <see cref="ReadinessStatusCodeMap"/>).
/// </summary>
public sealed class EventSubReadinessHealthCheckTests
{
    private static EventSourceHealth HealthOf(bool connected) =>
        new(connected, EventSubTransportKind.WebSocket, 5, null, null);

    [Fact]
    public async Task Connected_ReportsHealthy()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub.Health.Returns(HealthOf(connected: true));
        FakeTimeProvider clock = new();
        EventSubReadinessHealthCheck check = new(eventSub, new(clock));

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task BriefDisconnect_WithinGracePeriod_StaysHealthy_DoesNotFlapReadiness()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub.Health.Returns(HealthOf(connected: false));
        FakeTimeProvider clock = new();
        EventSubDisconnectTracker tracker = new(clock);
        EventSubReadinessHealthCheck check = new(eventSub, tracker);

        // First observation of the disconnect.
        HealthCheckResult first = await check.CheckHealthAsync(new());
        // Advance well within the normal reconnect-swap window (a graceful handover is sub-second to a
        // few seconds — nowhere near the 45s grace period, and far short of the ~5 minute reconnect cadence).
        clock.Advance(TimeSpan.FromSeconds(10));
        HealthCheckResult second = await check.CheckHealthAsync(new());

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task SustainedDisconnect_BeyondGracePeriod_ReportsDegraded()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub.Health.Returns(HealthOf(connected: false));
        FakeTimeProvider clock = new();
        EventSubDisconnectTracker tracker = new(clock);
        EventSubReadinessHealthCheck check = new(eventSub, tracker);

        await check.CheckHealthAsync(new());
        clock.Advance(EventSubDisconnectTracker.GracePeriod + TimeSpan.FromSeconds(1));
        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Reconnecting_ThenSustainedDrop_ThenReconnects_ResetsToHealthy()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        FakeTimeProvider clock = new();
        EventSubDisconnectTracker tracker = new(clock);
        EventSubReadinessHealthCheck check = new(eventSub, tracker);

        eventSub.Health.Returns(HealthOf(connected: false));
        await check.CheckHealthAsync(new());
        clock.Advance(EventSubDisconnectTracker.GracePeriod + TimeSpan.FromSeconds(1));
        (await check.CheckHealthAsync(new())).Status.Should().Be(HealthStatus.Degraded);

        eventSub.Health.Returns(HealthOf(connected: true));
        HealthCheckResult reconnected = await check.CheckHealthAsync(new());

        reconnected.Status.Should().Be(HealthStatus.Healthy);
    }
}
