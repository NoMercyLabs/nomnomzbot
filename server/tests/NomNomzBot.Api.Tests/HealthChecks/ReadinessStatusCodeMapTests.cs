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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Api.HealthChecks;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Proves the S116 fix: <c>/health/ready</c> (which is configured from
/// <see cref="ReadinessStatusCodeMap.Value"/> in <c>Program.cs</c>) fails an orchestrator's readiness probe
/// on a Degraded OR Unhealthy dependency — only Healthy reads as ready. Before this fix, Degraded mapped to
/// 200 (the same table the detail <c>/health</c> endpoint still uses), so a degraded dependency read as
/// "ready" to a caller that only checks the status code.
/// </summary>
public sealed class ReadinessStatusCodeMapTests
{
    [Fact]
    public void Degraded_MapsTo503_NotThePermissive200ThatHealthStillUses()
    {
        ReadinessStatusCodeMap
            .Value[HealthStatus.Degraded]
            .Should()
            .Be(
                StatusCodes.Status503ServiceUnavailable,
                "a degraded dependency must fail readiness even though /health's own mapping still reports it as 200"
            );
    }

    [Fact]
    public void Unhealthy_MapsTo503()
    {
        ReadinessStatusCodeMap
            .Value[HealthStatus.Unhealthy]
            .Should()
            .Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void Healthy_MapsTo200()
    {
        ReadinessStatusCodeMap.Value[HealthStatus.Healthy].Should().Be(StatusCodes.Status200OK);
    }
}
