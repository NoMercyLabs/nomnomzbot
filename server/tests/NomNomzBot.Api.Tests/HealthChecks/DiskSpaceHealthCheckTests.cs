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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Api.HealthChecks;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// The dev box disk filled twice (2026-11-10, 2026-11-19) and nobody saw it coming. The disk check warns the
/// owner in the health panel while there is still room, and never fails readiness, so a full disk can not
/// itself stop a deploy.
/// </summary>
public sealed class DiskSpaceHealthCheckTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public async Task Under_ten_gigabytes_free_it_warns_with_the_free_space_in_the_message()
    {
        DiskSpaceHealthCheck check = new(() => 9 * Gb);

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("9 GB free");
        result.Data["freeGb"].Should().Be(9L);
    }

    [Fact]
    public async Task With_room_to_spare_it_reports_healthy_and_the_free_space()
    {
        DiskSpaceHealthCheck check = new(() => 30 * Gb);

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["freeGb"].Should().Be(30L);
    }

    [Fact]
    public async Task A_disk_it_can_not_read_is_a_warning_not_a_crash()
    {
        DiskSpaceHealthCheck check = new(() => throw new IOException("no such mount"));

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("no such mount");
    }
}
