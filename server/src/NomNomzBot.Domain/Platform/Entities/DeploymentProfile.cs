// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// The single-row, GLOBAL deployment profile (schema P.12). Written once at boot by the deployment-profile
/// detector (platform-conventions §3.3): the auto-detected (or operator-overridden) <see cref="Mode"/> and the
/// adapter kinds it resolves to (DB / cache / EventSub transport / executor / token vault / exposure). Every
/// swappable adapter is DI-selected from this row. Not tenant-scoped, not soft-deletable — exactly one row exists.
/// </summary>
public class DeploymentProfile : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>A stable per-install identity (advertised over mDNS, used by the native connection switcher to de-dupe).</summary>
    public Guid InstanceId { get; set; } = Guid.CreateVersion7();

    public DeploymentMode Mode { get; set; }

    /// <summary>True when the mode was probed (Docker/Postgres/Redis reachability), false when forced by config/env override.</summary>
    public bool WasAutoDetected { get; set; }

    public DbProviderKind DbProvider { get; set; }
    public CacheProviderKind CacheProvider { get; set; }
    public EventSubTransportMode EventSubTransport { get; set; }
    public CodeExecutorKind CodeExecutor { get; set; }
    public TokenVaultKind TokenVault { get; set; }
    public ExposureModelKind ExposureModel { get; set; }

    /// <summary>True only for the Postgres profile (RLS is SaaS/full-only; SQLite falls back to app-level tenant filters).</summary>
    public bool RlsEnabled { get; set; }

    /// <summary>The first-run "Simple vs Advanced" wizard seed default for new users; <c>Novice</c> when bypassed.</summary>
    public GuidanceLevel DefaultGuidanceLevel { get; set; } = GuidanceLevel.Novice;
}
