// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// Boot-time detector + runtime accessor for the deployment profile (platform-conventions §3.3,
/// deployment-distribution §2). Resolves the mode from an explicit override or by probing infra (Postgres / Redis
/// reachability), maps it to every adapter kind, upserts the single-row <see cref="DeploymentProfile"/> (P.12),
/// probes host capabilities for first-run sizing, and emits <see cref="DeploymentProfileResolvedEvent"/>.
/// Registered as a singleton; it creates its own DI scope to touch the scoped <see cref="AppDbContext"/>.
/// </summary>
public sealed class DeploymentProfileService : IDeploymentProfileService
{
    // Honor both the documented App__DeploymentMode and the DEPLOYMENT__MODE override spelling.
    private const string AppModeKey = "App:DeploymentMode";
    private const string DeploymentModeKey = "Deployment:Mode";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IInfraReachabilityProbe _probe;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DeploymentProfileService> _logger;

    private DeploymentProfileSnapshot? _current;

    public DeploymentProfileService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IInfraReachabilityProbe probe,
        IEventBus eventBus,
        ILogger<DeploymentProfileService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _probe = probe;
        _eventBus = eventBus;
        _logger = logger;
    }

    public DeploymentProfileSnapshot Current =>
        _current
        ?? throw new InvalidOperationException(
            "Deployment profile read before DetectAndPersistAsync resolved it (fail-closed boot ordering)."
        );

    public async Task<Result<DeploymentProfileSnapshot>> DetectAndPersistAsync(
        CancellationToken cancellationToken = default
    )
    {
        (DeploymentMode mode, bool wasAutoDetected) = await ResolveModeAsync(cancellationToken);

        ProbeHostCapabilities();

        DeploymentProfileSnapshot snapshot = BuildSnapshot(mode, wasAutoDetected);

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        DeploymentProfile row =
            await db.DeploymentProfiles.FirstOrDefaultAsync(cancellationToken)
            ?? new DeploymentProfile { InstanceId = snapshot.InstanceId };

        // Preserve the persisted InstanceId across reboots so the bot keeps one stable LAN identity.
        Guid instanceId = row.InstanceId == Guid.Empty ? snapshot.InstanceId : row.InstanceId;
        snapshot = snapshot with { InstanceId = instanceId };

        row.InstanceId = instanceId;
        row.Mode = snapshot.Mode;
        row.WasAutoDetected = snapshot.WasAutoDetected;
        row.DbProvider = snapshot.DbProvider;
        row.CacheProvider = snapshot.CacheProvider;
        row.EventSubTransport = snapshot.EventSubTransport;
        row.CodeExecutor = snapshot.CodeExecutor;
        row.TokenVault = snapshot.TokenVault;
        row.ExposureModel = snapshot.ExposureModel;
        row.RlsEnabled = snapshot.RlsEnabled;
        row.DefaultGuidanceLevel = snapshot.DefaultGuidanceLevel;

        if (db.Entry(row).State == EntityState.Detached)
            db.DeploymentProfiles.Add(row);

        await db.SaveChangesAsync(cancellationToken);

        _current = snapshot;

        _logger.LogInformation(
            "Deployment profile resolved: mode={Mode} (auto-detected={Auto}), db={Db}, cache={Cache}, "
                + "eventsub={EventSub}, executor={Executor}, vault={Vault}, exposure={Exposure}, rls={Rls}, instance={Instance}",
            snapshot.Mode,
            snapshot.WasAutoDetected,
            snapshot.DbProvider,
            snapshot.CacheProvider,
            snapshot.EventSubTransport,
            snapshot.CodeExecutor,
            snapshot.TokenVault,
            snapshot.ExposureModel,
            snapshot.RlsEnabled,
            snapshot.InstanceId
        );

        await _eventBus.PublishAsync(
            new DeploymentProfileResolvedEvent
            {
                InstanceId = snapshot.InstanceId,
                Mode = snapshot.Mode.ToString(),
                WasAutoDetected = snapshot.WasAutoDetected,
                DbProvider = snapshot.DbProvider.ToString(),
                CacheProvider = snapshot.CacheProvider.ToString(),
                EventSubTransport = snapshot.EventSubTransport.ToString(),
                CodeExecutor = snapshot.CodeExecutor.ToString(),
                TokenVault = snapshot.TokenVault.ToString(),
                ExposureModel = snapshot.ExposureModel.ToString(),
                RlsEnabled = snapshot.RlsEnabled,
            },
            cancellationToken
        );

        return Result.Success(snapshot);
    }

    /// <summary>
    /// Resolves the mode WITHOUT any DB / event side effects — the pure decision the DI registration reads
    /// at startup (before the host is built) to bind the provider-specific adapters. Honors the override first,
    /// then probes infra reachability.
    /// </summary>
    public async Task<(DeploymentMode Mode, bool WasAutoDetected)> ResolveModeAsync(
        CancellationToken cancellationToken = default
    )
    {
        DeploymentMode? overridden = ReadModeOverride();
        if (overridden is { } forced)
        {
            _logger.LogInformation("Deployment mode forced to {Mode} by config override.", forced);
            return (forced, false);
        }

        bool postgresReachable = await _probe.IsPostgresReachableAsync(cancellationToken);
        bool redisReachable = await _probe.IsRedisReachableAsync(cancellationToken);

        // Full requires the durable data tier to actually be up; anything short of that runs lite (SQLite +
        // in-process cache/bus), which is the zero-dependency default.
        DeploymentMode detected =
            postgresReachable && redisReachable
                ? DeploymentMode.SelfHostFull
                : DeploymentMode.SelfHostLite;

        _logger.LogInformation(
            "Deployment mode auto-detected as {Mode} (postgres={Postgres}, redis={Redis}).",
            detected,
            postgresReachable,
            redisReachable
        );
        return (detected, true);
    }

    private DeploymentMode? ReadModeOverride()
    {
        string? raw = _configuration[DeploymentModeKey] ?? _configuration[AppModeKey];
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Accept both the snake_case spec spelling (self_host_lite) and the enum name (SelfHostLite).
        string normalized = raw.Replace("_", string.Empty).Trim();
        foreach (DeploymentMode candidate in Enum.GetValues<DeploymentMode>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        _logger.LogWarning(
            "Ignoring unrecognized deployment mode override '{Raw}'; falling back to auto-detection.",
            raw
        );
        return null;
    }

    private static DeploymentProfileSnapshot BuildSnapshot(
        DeploymentMode mode,
        bool wasAutoDetected
    )
    {
        bool isSaas = mode == DeploymentMode.Saas;
        bool isFull = mode == DeploymentMode.SelfHostFull;
        bool usesDurableTier = isSaas || isFull;

        return new DeploymentProfileSnapshot(
            InstanceId: Guid.CreateVersion7(),
            Mode: mode,
            WasAutoDetected: wasAutoDetected,
            DbProvider: usesDurableTier ? DbProviderKind.Postgres : DbProviderKind.Sqlite,
            CacheProvider: usesDurableTier ? CacheProviderKind.Redis : CacheProviderKind.InMemory,
            EventSubTransport: isSaas
                ? EventSubTransportMode.ConduitWebhook
                : EventSubTransportMode.WebSocket,
            CodeExecutor: isSaas ? CodeExecutorKind.Wasmtime : CodeExecutorKind.Jint,
            TokenVault: TokenVaultKind.LocalAes,
            ExposureModel: isSaas ? ExposureModelKind.ManagedEdge : ExposureModelKind.OptInTunnel,
            // RLS is Postgres-only; SQLite falls back to the app-level tenant query filter.
            RlsEnabled: usesDurableTier,
            DefaultGuidanceLevel: GuidanceLevel.Novice
        );
    }

    private void ProbeHostCapabilities()
    {
        int cores = Environment.ProcessorCount;
        long availableBytes = GetAvailableMemoryBytes();
        _logger.LogInformation(
            "Host capabilities: {Cores} CPU cores, ~{MemoryMb} MB available. First-run sizing uses these "
                + "as advisory inputs to the Scaling:* knobs (explicit overrides always win).",
            cores,
            availableBytes / (1024 * 1024)
        );
    }

    private static long GetAvailableMemoryBytes()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        // TotalAvailableMemoryBytes reflects the container/cgroup memory limit when constrained, else the box's RAM.
        return info.TotalAvailableMemoryBytes;
    }
}
