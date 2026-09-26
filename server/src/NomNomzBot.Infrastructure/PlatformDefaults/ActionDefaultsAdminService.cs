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
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// See <see cref="IActionDefaultsAdminService"/>. Writes <see cref="ActionDefinition.PlatformDefaultLevel"/>,
/// the column the seeder never touches; Gate 2 reads it through <see cref="ActionLevelPolicy.DefaultLevel"/>
/// on every check, so a save takes effect on the next request with no cache to flush.
/// </summary>
public sealed class ActionDefaultsAdminService(IApplicationDbContext db, TimeProvider clock)
    : IActionDefaultsAdminService
{
    private const string AuditFamily = "action_level";

    public async Task<Result<IReadOnlyList<ActionDefaultDto>>> ListAsync(
        CancellationToken ct = default
    )
    {
        List<ActionDefinition> actions = await db
            .ActionDefinitions.OrderBy(a => a.ActionKey)
            .ToListAsync(ct);
        Dictionary<Guid, int> overrideCounts = await LiveOverrides()
            .GroupBy(o => o.ActionDefinitionId)
            .Select(g => new { ActionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ActionId, x => x.Count, ct);

        List<ActionDefaultDto> rows =
        [
            .. actions.Select(a => ToDto(a, overrideCounts.GetValueOrDefault(a.Id))),
        ];
        return Result.Success<IReadOnlyList<ActionDefaultDto>>(rows);
    }

    public async Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string actionKey,
        int? level,
        CancellationToken ct = default
    )
    {
        ActionDefinition? action = await FindAsync(actionKey, ct);
        if (action is null)
            return Result.Failure<PlatformDefaultBlastRadiusDto>(
                $"Unknown action '{actionKey}'.",
                "NOT_FOUND"
            );

        Result validation = Validate(action, level);
        if (validation.IsFailure)
            return validation.WithValue<PlatformDefaultBlastRadiusDto>(null!);

        return Result.Success(await CountAsync(action, level, ct));
    }

    public async Task<Result<ActionDefaultDto>> SetAsync(
        string actionKey,
        SetActionDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        ActionDefinition? action = await FindAsync(actionKey, ct);
        if (action is null)
            return Result.Failure<ActionDefaultDto>($"Unknown action '{actionKey}'.", "NOT_FOUND");

        Result validation = Validate(action, request.Level);
        if (validation.IsFailure)
            return validation.WithValue<ActionDefaultDto>(null!);

        PlatformDefaultBlastRadiusDto radius = await CountAsync(action, request.Level, ct);
        if (radius.RequiresDangerConfirmation && !request.ConfirmDanger)
            return Result.Failure<ActionDefaultDto>(
                $"'{actionKey}' guards a dangerous action; the change must be confirmed explicitly.",
                "VALIDATION_FAILED"
            );
        if (radius.ChannelsAffected != request.ConfirmedChannelsAffected)
            return Result.Failure<ActionDefaultDto>(
                $"The change now affects {radius.ChannelsAffected} channels, not the {request.ConfirmedChannelsAffected} you confirmed. Review it again.",
                "PREVIEW_STALE"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        int oldLevel = ActionLevelPolicy.DefaultLevel(action);
        action.PlatformDefaultLevel = request.Level;
        action.PlatformDefaultSetByUserId = actorUserId;
        action.PlatformDefaultSetAt = now;
        PlatformDefaultAudit.Record(
            db,
            AuditFamily,
            actionKey,
            actorUserId,
            oldLevel.ToString(),
            ActionLevelPolicy.DefaultLevel(action).ToString(),
            radius.ChannelsAffected,
            now
        );
        await db.SaveChangesAsync(ct);

        // Read back from the store rather than echoing the tracked entity, so the caller sees what was
        // actually persisted.
        ActionDefinition saved = await db
            .ActionDefinitions.AsNoTracking()
            .FirstAsync(a => a.Id == action.Id, ct);
        int overrideCount = await LiveOverrides()
            .CountAsync(o => o.ActionDefinitionId == action.Id, ct);
        return Result.Success(ToDto(saved, overrideCount));
    }

    /// <summary>
    /// A platform default is a named ladder rung at or above the action's floor, or null (back to the shipped
    /// default). The ceiling is Broadcaster — nothing above it exists.
    /// </summary>
    private static Result Validate(ActionDefinition action, int? level)
    {
        if (level is not { } value)
            return Result.Success();
        if (AuthorizationLadder.FromLevelValue(value).ToLevelValue() != value)
            return Result.Failure(
                $"Level {value} is not a role on the ladder.",
                "VALIDATION_FAILED"
            );
        if (value < action.FloorLevel)
            return Result.Failure(
                $"Level {value} is below the action floor {action.FloorLevel}.",
                "VALIDATION_FAILED"
            );
        return Result.Success();
    }

    private async Task<PlatformDefaultBlastRadiusDto> CountAsync(
        ActionDefinition action,
        int? level,
        CancellationToken ct
    )
    {
        int current = ActionLevelPolicy.DefaultLevel(action);
        int proposed = Math.Max(level ?? action.DefaultLevel, action.FloorLevel);
        IQueryable<Guid> withOverride = LiveOverrides()
            .Where(o => o.ActionDefinitionId == action.Id)
            .Select(o => o.BroadcasterId);
        return await PlatformDefaultBlastRadius.CountAsync(
            db,
            withOverride,
            valueChanges: proposed != current,
            requiresDangerConfirmation: action.FloorTier != DangerTier.Low,
            ct
        );
    }

    // The admin request carries no channel target, so the tenant filter would narrow this to the operator's own
    // channel; the platform view reads every channel's live override explicitly.
    private IQueryable<ChannelActionOverride> LiveOverrides() =>
        db.ChannelActionOverrides.IgnoreQueryFilters().Where(o => o.DeletedAt == null);

    private async Task<ActionDefinition?> FindAsync(string actionKey, CancellationToken ct) =>
        await db.ActionDefinitions.FirstOrDefaultAsync(a => a.ActionKey == actionKey, ct);

    private static ActionDefaultDto ToDto(ActionDefinition a, int overrideCount) =>
        new(
            a.ActionKey,
            a.Plane,
            a.Description,
            a.DefaultLevel,
            a.PlatformDefaultLevel,
            ActionLevelPolicy.DefaultLevel(a),
            a.FloorLevel,
            a.FloorTier,
            overrideCount
        );
}
