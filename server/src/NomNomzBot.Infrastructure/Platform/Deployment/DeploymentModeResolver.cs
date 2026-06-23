// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// Resolves the deployment mode at DI-registration time — BEFORE the host is built — so the provider-specific
/// adapters (DB provider, cache, bus, KEK, run-once, rate-limiter store) can be bound from it (deployment-distribution
/// §2 steps 1–2). This is the same override-first, probe-second decision the runtime
/// <see cref="DeploymentProfileService"/> makes; the service later persists the row + emits the event against the
/// already-correctly-bound adapters. Synchronous (registration is synchronous): the infra probe is bridged with a
/// short bounded wait.
/// </summary>
public static class DeploymentModeResolver
{
    /// <summary>
    /// The resolved mode plus whether it was auto-detected (false ⇒ forced by config/env override).
    /// </summary>
    public static (DeploymentMode Mode, bool WasAutoDetected) Resolve(IConfiguration configuration)
    {
        DeploymentMode? overridden = ReadModeOverride(configuration);
        if (overridden is { } forced)
            return (forced, false);

        InfraReachabilityProbe probe = new(
            configuration,
            NullLogger<InfraReachabilityProbe>.Instance
        );

        // Registration is synchronous; the probe is bounded (~1.5s timeout each). Block briefly — this runs once
        // at boot before any traffic.
        bool postgresReachable = probe.IsPostgresReachableAsync().GetAwaiter().GetResult();
        bool redisReachable = probe.IsRedisReachableAsync().GetAwaiter().GetResult();

        DeploymentMode detected =
            postgresReachable && redisReachable
                ? DeploymentMode.SelfHostFull
                : DeploymentMode.SelfHostLite;

        return (detected, true);
    }

    public static DbProviderKind DbProviderFor(DeploymentMode mode) =>
        mode is DeploymentMode.SelfHostFull or DeploymentMode.Saas
            ? DbProviderKind.Postgres
            : DbProviderKind.Sqlite;

    public static CacheProviderKind CacheProviderFor(DeploymentMode mode) =>
        mode is DeploymentMode.SelfHostFull or DeploymentMode.Saas
            ? CacheProviderKind.Redis
            : CacheProviderKind.InMemory;

    private static DeploymentMode? ReadModeOverride(IConfiguration configuration)
    {
        string? raw = configuration["Deployment:Mode"] ?? configuration["App:DeploymentMode"];
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string normalized = raw.Replace("_", string.Empty).Trim();
        foreach (DeploymentMode candidate in Enum.GetValues<DeploymentMode>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }
}
