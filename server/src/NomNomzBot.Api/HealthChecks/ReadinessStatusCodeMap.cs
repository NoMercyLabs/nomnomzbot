// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// The HTTP status-code mapping actually wired onto <c>/health/ready</c> (S116). Extracted to a plain,
/// directly-testable dictionary rather than an inline anonymous initializer inside <c>Program.cs</c>, so a
/// test can assert the real orchestrator-facing contract: a <see cref="HealthStatus.Degraded"/> or
/// <see cref="HealthStatus.Unhealthy"/> dependency must fail readiness (503), while only
/// <see cref="HealthStatus.Healthy"/> reads as ready (200). This is intentionally stricter than the
/// <c>/health</c> detail endpoint's mapping, which still reports Degraded as 200 so its JSON body remains
/// reachable for an operator to inspect which component is degraded — <c>/health/live</c> is untouched by
/// either of these and always answers 200 while the process is alive.
/// </summary>
public static class ReadinessStatusCodeMap
{
    public static readonly IReadOnlyDictionary<HealthStatus, int> Value = new Dictionary<
        HealthStatus,
        int
    >
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    };
}
