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
