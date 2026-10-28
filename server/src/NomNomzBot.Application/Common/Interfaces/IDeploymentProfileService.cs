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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// The boot-time deployment-profile detector + the runtime accessor for the resolved profile
/// (platform-conventions §3.3). <see cref="DetectAndPersistAsync"/> probes infra (Docker / Postgres / Redis
/// reachable, honoring an explicit <c>DEPLOYMENT__MODE</c> / <c>App__DeploymentMode</c> override), resolves every
/// adapter kind, upserts the single-row <c>DeploymentProfile</c> (P.12), and emits
/// <c>DeploymentProfileResolvedEvent</c>. It runs once at boot, before adapter binding / migrations / KEK.
/// </summary>
public interface IDeploymentProfileService
{
    /// <summary>The immutable resolved profile. Throws if read before <see cref="DetectAndPersistAsync"/> completes (fail-closed boot).</summary>
    DeploymentProfileSnapshot Current { get; }

    /// <summary>
    /// Detects (or loads) and persists the deployment profile. Idempotent: re-running returns the persisted row,
    /// re-detecting the live infra each boot so a moved install (e.g. SQLite → Postgres) re-resolves correctly.
    /// </summary>
    Task<Result<DeploymentProfileSnapshot>> DetectAndPersistAsync(
        CancellationToken cancellationToken = default
    );
}

/// <summary>The immutable, resolved deployment profile the DI adapter registry branches on (platform-conventions §3.3).</summary>
public sealed record DeploymentProfileSnapshot(
    Guid InstanceId,
    DeploymentMode Mode,
    bool WasAutoDetected,
    DbProviderKind DbProvider,
    CacheProviderKind CacheProvider,
    EventSubTransportMode EventSubTransport,
    CodeExecutorKind CodeExecutor,
    TokenVaultKind TokenVault,
    ExposureModelKind ExposureModel,
    bool RlsEnabled,
    GuidanceLevel DefaultGuidanceLevel
);
