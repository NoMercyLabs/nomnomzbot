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

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// Raised once at boot after the deployment profile is detected/loaded and the adapter registry is built
/// (platform-conventions §2 / §3.3). Platform-level — <c>BroadcasterId</c> stays <c>Guid.Empty</c>. Carries the
/// resolved mode + every adapter kind so observers (diagnostics, health, telemetry scope) see the live shape.
/// </summary>
public sealed class DeploymentProfileResolvedEvent : DomainEventBase
{
    /// <summary>The unique id of this bot installation.</summary>
    public required Guid InstanceId { get; init; }

    /// <summary>The deployment mode this installation runs in.</summary>
    public required string Mode { get; init; }

    /// <summary>True when the bot chose the mode itself. False when the owner set it.</summary>
    public required bool WasAutoDetected { get; init; }

    /// <summary>The kind of database in use.</summary>
    public required string DbProvider { get; init; }

    /// <summary>The kind of cache in use.</summary>
    public required string CacheProvider { get; init; }

    /// <summary>How the bot receives Twitch events: WebSocket or ConduitWebhook.</summary>
    public required string EventSubTransport { get; init; }

    /// <summary>Where custom scripts run.</summary>
    public required string CodeExecutor { get; init; }

    /// <summary>Where login tokens are stored.</summary>
    public required string TokenVault { get; init; }

    /// <summary>How the installation is reachable from the internet.</summary>
    public required string ExposureModel { get; init; }

    /// <summary>True when row-level security is on in the database.</summary>
    public required bool RlsEnabled { get; init; }
}
