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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Gate 2 (roles-permissions §3.2–3.3): authorizes a caller against an action and owns the per-channel override
/// config. The action's effective required level is <c>clamp(override ?? default, floor, Broadcaster)</c>; a
/// caller is allowed when EITHER their resolved level (<see cref="IRoleResolver"/>) meets it OR the broadcaster
/// issued them a direct per-user capability grant for exactly this action. That capability path is how a
/// broadcaster delegates an above-floor, permit-grantable action (e.g. <c>channel:title:write</c>) to a
/// specific moderator — the bot then performs it on the broadcaster's own token. It is the single HTTP mirror of
/// <see cref="IRoleResolver.HasCapabilityAsync"/> (which also gates chat commands), so a delegated capability
/// works from the dashboard, not only in chat. Denials and override changes publish their domain events;
/// unknown action keys fail closed.
/// </summary>
public sealed class ActionAuthorizationService(
    IApplicationDbContext db,
    IRoleResolver roleResolver,
    IEventBus eventBus,
    TimeProvider clock
) : IActionAuthorizationService
{
    private const int BroadcasterLevel = 40;

    public async Task<Result<bool>> AuthorizeActionAsync(
        Guid userId,
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await FindActionAsync(actionKey, cancellationToken);
        if (action is null)
            return Result.Success(false); // unknown key → fail closed

        int required = await EffectiveLevelAsync(broadcasterId, action, cancellationToken);
        Result<int> resolved = await roleResolver.ResolveEffectiveLevelAsync(
            userId,
            broadcasterId,
            cancellationToken
        );
        int callerLevel = resolved.IsSuccess ? resolved.Value : 0;

        if (callerLevel >= required)
            return Result.Success(true);

        // Below the level bar, but a broadcaster can still delegate this exact action to this exact user via a
        // direct capability grant (roles-permissions §3.2/§3.6). HasCapabilityAsync is the canonical allow rule
        // and is bounded by construction: a grant can only exist for an IsGrantableViaPermit action, so a
        // non-delegable Critical action (e.g. moderation:nuke) can never be reached this way.
        Result<bool> capability = await roleResolver.HasCapabilityAsync(
            userId,
            broadcasterId,
            actionKey,
            cancellationToken
        );
        if (capability.IsSuccess && capability.Value)
            return Result.Success(true);

        await eventBus.PublishAsync(
            new AuthorizationDeniedEvent
            {
                BroadcasterId = broadcasterId,
                CallerUserId = userId,
                ActionKey = actionKey,
                RequiredLevel = required,
                CallerLevel = callerLevel,
                Gate = "gate2",
            },
            cancellationToken
        );
        return Result.Success(false);
    }

    public async Task<Result<int>> GetEffectiveLevelAsync(
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await FindActionAsync(actionKey, cancellationToken);
        if (action is null)
            return Result.Failure<int>($"Unknown action '{actionKey}'.", "NOT_FOUND");
        return Result.Success(await EffectiveLevelAsync(broadcasterId, action, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<ActionPermissionDto>>> GetActionMatrixAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ActionDefinition> actions = await db
            .ActionDefinitions.OrderBy(a => a.ActionKey)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, int> overrides = await db
            .ChannelActionOverrides.Where(o =>
                o.BroadcasterId == broadcasterId && o.DeletedAt == null
            )
            .ToDictionaryAsync(o => o.ActionDefinitionId, o => o.OverrideLevel, cancellationToken);

        List<ActionPermissionDto> matrix =
        [
            .. actions.Select(a =>
            {
                int? ov = overrides.TryGetValue(a.Id, out int v) ? v : null;
                int effective = ActionLevelPolicy.EffectiveRequiredLevel(a, ov);
                return new ActionPermissionDto(
                    a.Id,
                    a.ActionKey,
                    a.Plane,
                    a.Description,
                    a.DefaultLevel,
                    a.FloorLevel,
                    a.FloorTier,
                    a.IsGrantableViaPermit,
                    ov,
                    effective
                );
            }),
        ];
        return Result.Success<IReadOnlyList<ActionPermissionDto>>(matrix);
    }

    public async Task<Result<int>> SetActionOverrideAsync(
        Guid broadcasterId,
        string actionKey,
        int level,
        Guid setByUserId,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await FindActionAsync(actionKey, cancellationToken);
        if (action is null)
            return Result.Failure<int>($"Unknown action '{actionKey}'.", "NOT_FOUND");
        if (level < action.FloorLevel)
            return Result.Failure<int>(
                $"Level {level} is below the action floor {action.FloorLevel}.",
                "VALIDATION_FAILED"
            );

        int clamped = Math.Clamp(level, action.FloorLevel, BroadcasterLevel);

        ChannelActionOverride? existing = await FindOverrideAsync(
            broadcasterId,
            action.Id,
            cancellationToken
        );
        int? oldLevel = existing?.OverrideLevel;
        if (existing is null)
        {
            db.ChannelActionOverrides.Add(
                new ChannelActionOverride
                {
                    BroadcasterId = broadcasterId,
                    ActionDefinitionId = action.Id,
                    OverrideLevel = clamped,
                    SetByUserId = setByUserId,
                }
            );
        }
        else
        {
            existing.OverrideLevel = clamped;
            existing.SetByUserId = setByUserId;
        }
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new ActionLevelOverriddenEvent
            {
                BroadcasterId = broadcasterId,
                ActionDefinitionId = action.Id,
                ActionKey = actionKey,
                OldLevel = oldLevel,
                NewEffectiveLevel = clamped,
                SetByUserId = setByUserId,
            },
            cancellationToken
        );
        return Result.Success(clamped);
    }

    public async Task<Result> ResetActionOverrideAsync(
        Guid broadcasterId,
        string actionKey,
        Guid setByUserId,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await FindActionAsync(actionKey, cancellationToken);
        if (action is null)
            return Result.Failure($"Unknown action '{actionKey}'.", "NOT_FOUND");

        ChannelActionOverride? existing = await FindOverrideAsync(
            broadcasterId,
            action.Id,
            cancellationToken
        );
        int? oldLevel = existing?.OverrideLevel;
        if (existing is not null)
        {
            existing.DeletedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }

        await eventBus.PublishAsync(
            new ActionLevelOverriddenEvent
            {
                BroadcasterId = broadcasterId,
                ActionDefinitionId = action.Id,
                ActionKey = actionKey,
                OldLevel = oldLevel,
                NewEffectiveLevel = action.DefaultLevel,
                SetByUserId = setByUserId,
            },
            cancellationToken
        );
        return Result.Success();
    }

    private async Task<ActionDefinition?> FindActionAsync(string actionKey, CancellationToken ct) =>
        await db.ActionDefinitions.Where(a => a.ActionKey == actionKey).FirstOrDefaultAsync(ct);

    private async Task<ChannelActionOverride?> FindOverrideAsync(
        Guid broadcasterId,
        Guid actionDefinitionId,
        CancellationToken ct
    ) =>
        await db
            .ChannelActionOverrides.Where(o =>
                o.BroadcasterId == broadcasterId
                && o.ActionDefinitionId == actionDefinitionId
                && o.DeletedAt == null
            )
            .FirstOrDefaultAsync(ct);

    private async Task<int> EffectiveLevelAsync(
        Guid broadcasterId,
        ActionDefinition action,
        CancellationToken ct
    )
    {
        ChannelActionOverride? ov = await FindOverrideAsync(broadcasterId, action.Id, ct);
        return ActionLevelPolicy.EffectiveRequiredLevel(action, ov?.OverrideLevel);
    }
}
