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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>The feature-flag admin write surface (rollout-updates §5) — drives staged rollout + per-tenant overrides.</summary>
public sealed class FeatureFlagAdminService(
    IApplicationDbContext db,
    IEventBus eventBus,
    ICacheService cache,
    TimeProvider clock
) : IFeatureFlagAdminService
{
    public async Task<Result<IReadOnlyList<FeatureFlagDto>>> ListAsync(
        CancellationToken ct = default
    )
    {
        List<FeatureFlagDto> flags = await db
            .FeatureFlags.OrderBy(f => f.Key)
            .Select(f => new FeatureFlagDto(
                f.Key,
                f.Description,
                f.IsEnabledGlobally,
                f.RolloutPercentage,
                f.MinTierKey,
                f.RequiresConsent,
                f.DeploymentMode
            ))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<FeatureFlagDto>>(flags);
    }

    public async Task<Result<FeatureFlagDto>> SetFlagAsync(
        SetFeatureFlagRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Key) || request.RolloutPercentage is < 0 or > 100)
            return Result.Failure<FeatureFlagDto>(
                "Key is required and rollout must be 0–100.",
                "VALIDATION_FAILED"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        FeatureFlag? flag = await db.FeatureFlags.FirstOrDefaultAsync(
            f => f.Key == request.Key,
            ct
        );
        string oldValue = flag is null
            ? "(new)"
            : $"enabled={flag.IsEnabledGlobally};rollout={flag.RolloutPercentage};tier={flag.MinTierKey ?? "-"}";
        if (flag is null)
        {
            flag = new() { Key = request.Key, CreatedAt = now };
            db.FeatureFlags.Add(flag);
        }

        flag.Description = request.Description;
        flag.IsEnabledGlobally = request.IsEnabledGlobally;
        flag.RolloutPercentage = request.RolloutPercentage;
        flag.RequiresConsent = request.RequiresConsent;
        flag.DeploymentMode = request.DeploymentMode;
        flag.MinTierKey = request.MinTierKey;
        flag.MinTierId = request.MinTierKey is null
            ? null
            : await db
                .BillingTiers.Where(t => t.Key == request.MinTierKey)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);
        flag.UpdatedAt = now;

        string newValue =
            $"enabled={flag.IsEnabledGlobally};rollout={flag.RolloutPercentage};tier={flag.MinTierKey ?? "-"}";
        AuditFlagChange(request.Key, null, actorUserId, oldValue, newValue, now);

        await db.SaveChangesAsync(ct);
        await EmitAsync(request.Key, Guid.Empty, "flag_set", actorUserId, ct);

        return Result.Success(
            new FeatureFlagDto(
                flag.Key,
                flag.Description,
                flag.IsEnabledGlobally,
                flag.RolloutPercentage,
                flag.MinTierKey,
                flag.RequiresConsent,
                flag.DeploymentMode
            )
        );
    }

    public async Task<Result> SetOverrideAsync(
        string flagKey,
        Guid broadcasterId,
        SetFeatureFlagOverrideRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        FeatureFlag? flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == flagKey, ct);
        if (flag is null)
            return Result.Failure("Feature flag not found.", "NOT_FOUND");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        FeatureFlagOverride? over = await db.FeatureFlagOverrides.FirstOrDefaultAsync(
            o => o.FeatureFlagId == flag.Id && o.BroadcasterId == broadcasterId,
            ct
        );
        bool wasNew = over is null;
        string oldValue = wasNew ? "(none)" : $"enabled={over!.IsEnabled}";
        if (over is null)
        {
            over = new()
            {
                FeatureFlagId = flag.Id,
                BroadcasterId = broadcasterId,
                CreatedAt = now,
            };
            db.FeatureFlagOverrides.Add(over);
        }
        over.IsEnabled = request.IsEnabled;
        over.Reason = request.Reason;
        over.ExpiresAt = request.ExpiresAt;
        over.UpdatedAt = now;

        AuditFlagChange(
            flagKey,
            broadcasterId,
            actorUserId,
            oldValue,
            $"enabled={over.IsEnabled}",
            now
        );

        await db.SaveChangesAsync(ct);
        await InvalidateAsync(flagKey, broadcasterId, ct);
        await EmitAsync(flagKey, broadcasterId, "override_set", actorUserId, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveOverrideAsync(
        string flagKey,
        Guid broadcasterId,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        FeatureFlag? flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == flagKey, ct);
        if (flag is null)
            return Result.Failure("Feature flag not found.", "NOT_FOUND");

        FeatureFlagOverride? over = await db.FeatureFlagOverrides.FirstOrDefaultAsync(
            o => o.FeatureFlagId == flag.Id && o.BroadcasterId == broadcasterId,
            ct
        );
        if (over is null)
            return Result.Failure("Override not found.", "NOT_FOUND");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        AuditFlagChange(
            flagKey,
            broadcasterId,
            actorUserId,
            $"enabled={over.IsEnabled}",
            "(removed)",
            now
        );

        db.FeatureFlagOverrides.Remove(over);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(flagKey, broadcasterId, ct);
        await EmitAsync(flagKey, broadcasterId, "override_removed", actorUserId, ct);
        return Result.Success();
    }

    private Task InvalidateAsync(string flagKey, Guid broadcasterId, CancellationToken ct) =>
        cache.RemoveAsync($"ff:{flagKey}:{broadcasterId}", ct);

    /// <summary>
    /// Records a feature-flag mutation into the platform-IAM audit log (S086f — flag changes were entirely
    /// unaudited). Reuses <see cref="IamAuditLog"/> rather than a second audit table: <c>Permission</c>
    /// carries the flag action, <c>TargetResource</c> the flag key, and <c>Justification</c> the actor plus
    /// the before/after values. Tracked on the change tracker for the caller's own <c>SaveChangesAsync</c>.
    /// </summary>
    private void AuditFlagChange(
        string flagKey,
        Guid? broadcasterId,
        Guid? actorUserId,
        string oldValue,
        string newValue,
        DateTime now
    ) =>
        db.IamAuditLogs.Add(
            new()
            {
                PrincipalId = actorUserId ?? Guid.Empty,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "feature_flag:set",
                TargetBroadcasterId = broadcasterId,
                TargetResource = flagKey,
                Justification =
                    $"actor={actorUserId?.ToString() ?? "system"};old={oldValue};new={newValue}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
            }
        );

    private async Task EmitAsync(
        string flagKey,
        Guid broadcasterId,
        string action,
        Guid? actorUserId,
        CancellationToken ct
    )
    {
        await eventBus.PublishAsync(
            new FeatureFlagAdministeredEvent
            {
                FlagKey = flagKey,
                Action = action,
                ActorUserId = actorUserId,
                BroadcasterId = broadcasterId,
            },
            ct
        );
        await eventBus.PublishAsync(
            new FeatureFlagChangedEvent { FlagKey = flagKey, BroadcasterId = broadcasterId },
            ct
        );
    }
}
