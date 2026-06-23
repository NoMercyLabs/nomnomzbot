// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// Raised once at boot after the deployment profile is detected/loaded and the adapter registry is built
/// (platform-conventions §2 / §3.3). Platform-level — <c>BroadcasterId</c> stays <c>Guid.Empty</c>. Carries the
/// resolved mode + every adapter kind so observers (diagnostics, health, telemetry scope) see the live shape.
/// </summary>
public sealed class DeploymentProfileResolvedEvent : DomainEventBase
{
    public required Guid InstanceId { get; init; }
    public required string Mode { get; init; }
    public required bool WasAutoDetected { get; init; }
    public required string DbProvider { get; init; }
    public required string CacheProvider { get; init; }
    public required string EventSubTransport { get; init; }
    public required string CodeExecutor { get; init; }
    public required string TokenVault { get; init; }
    public required string ExposureModel { get; init; }
    public required bool RlsEnabled { get; init; }
}
