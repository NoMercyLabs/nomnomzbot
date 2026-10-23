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
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord;

/// <inheritdoc cref="IDiscordLiveRoleConfigService" />
public sealed class DiscordLiveRoleConfigService : IDiscordLiveRoleConfigService
{
    private const int MaxIdLength = 50;

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _eventBus;

    public DiscordLiveRoleConfigService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IEventBus eventBus
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
    }

    public async Task<Result<IReadOnlyList<DiscordLiveRoleConfigDto>>> GetAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        List<DiscordLiveRoleConfig> rows = await _db
            .DiscordLiveRoleConfigs.Where(c =>
                c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId
            )
            .ToListAsync(ct);

        IReadOnlyList<DiscordLiveRoleConfigDto> dtos = [.. rows.Select(ToDto)];
        return Result.Success(dtos);
    }

    public async Task<Result<DiscordLiveRoleConfigDto>> CreateAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordLiveRoleConfigRequest request,
        CancellationToken ct = default
    )
    {
        Result validation = Validate(request.RoleId, request.DiscordMemberId);
        if (validation.IsFailure)
            return Result.Failure<DiscordLiveRoleConfigDto>(
                validation.ErrorMessage,
                validation.ErrorCode
            );

        DiscordGuildConnection? connection = await _db.DiscordGuildConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );
        if (connection is null)
            return Errors.NotFound<DiscordLiveRoleConfigDto>(
                "Discord connection",
                connectionId.ToString()
            );

        Result roleFree = await EnsureRoleNotSelfAssignAsync(connectionId, request.RoleId, ct);
        if (roleFree.IsFailure)
            return Result.Failure<DiscordLiveRoleConfigDto>(
                roleFree.ErrorMessage,
                roleFree.ErrorCode
            );

        string? memberId = request.DiscordMemberId?.Trim() ?? connection.ApprovedByDiscordUserId;
        if (string.IsNullOrEmpty(memberId))
            return NeedsTwitchLink<DiscordLiveRoleConfigDto>();

        // Soft-deleted rows included: a removed rule keeps its (channel, server) row, and the unique index still
        // counts it, so setting the rule up again brings that row back instead of inserting a second one.
        DiscordLiveRoleConfig? row = await _db
            .DiscordLiveRoleConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId,
                ct
            );
        if (row is { DeletedAt: null })
            return Result.Failure<DiscordLiveRoleConfigDto>(
                "A live-role rule already exists for this connection.",
                "ALREADY_EXISTS"
            );

        if (row is null)
        {
            row = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                GuildConnectionId = connectionId,
            };
            _db.DiscordLiveRoleConfigs.Add(row);
        }

        row.DeletedAt = null;
        row.DeletedBy = null;
        row.IsCurrentlyApplied = false;
        row.RoleId = request.RoleId.Trim();
        row.DiscordMemberId = memberId;
        row.Enabled = request.Enabled;
        await _unitOfWork.SaveChangesAsync(ct);
        await DiscordConfigChangePublisher.PublishAsync(
            _eventBus,
            broadcasterId,
            row.Id,
            "created",
            ct
        );

        return Result.Success(ToDto(row));
    }

    public async Task<Result<DiscordLiveRoleConfigDto>> UpdateAsync(
        Guid broadcasterId,
        Guid connectionId,
        UpdateDiscordLiveRoleConfigRequest request,
        CancellationToken ct = default
    )
    {
        DiscordLiveRoleConfig? row = await FindAsync(broadcasterId, connectionId, ct);
        if (row is null)
            return Errors.NotFound<DiscordLiveRoleConfigDto>(
                "Discord live-role rule",
                connectionId.ToString()
            );

        Result validation = Validate(request.RoleId, request.DiscordMemberId);
        if (validation.IsFailure)
            return Result.Failure<DiscordLiveRoleConfigDto>(
                validation.ErrorMessage,
                validation.ErrorCode
            );

        string roleId = request.RoleId.Trim();
        string memberId = request.DiscordMemberId?.Trim() ?? row.DiscordMemberId;
        if (roleId != row.RoleId)
        {
            Result roleFree = await EnsureRoleNotSelfAssignAsync(connectionId, roleId, ct);
            if (roleFree.IsFailure)
                return Result.Failure<DiscordLiveRoleConfigDto>(
                    roleFree.ErrorMessage,
                    roleFree.ErrorCode
                );
        }

        bool targetChanged = roleId != row.RoleId || memberId != row.DiscordMemberId;
        if (row.IsCurrentlyApplied && targetChanged)
            return Result.Failure<DiscordLiveRoleConfigDto>(
                "The live role is applied right now. Change the role or member after the stream ends.",
                "LIVE_ROLE_IN_USE"
            );

        row.RoleId = roleId;
        row.DiscordMemberId = memberId;
        row.Enabled = request.Enabled;
        await _unitOfWork.SaveChangesAsync(ct);
        await DiscordConfigChangePublisher.PublishAsync(
            _eventBus,
            broadcasterId,
            row.Id,
            "updated",
            ct
        );

        return Result.Success(ToDto(row));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordLiveRoleConfig? row = await FindAsync(broadcasterId, connectionId, ct);
        if (row is null)
            return Errors.NotFound<object>("Discord live-role rule", connectionId.ToString());

        if (row.IsCurrentlyApplied)
            return Result.Failure(
                "The live role is applied right now. Delete the rule after the stream ends.",
                "LIVE_ROLE_IN_USE"
            );

        _db.DiscordLiveRoleConfigs.Remove(row); // soft-delete via interceptor
        await _unitOfWork.SaveChangesAsync(ct);
        await DiscordConfigChangePublisher.PublishAsync(
            _eventBus,
            broadcasterId,
            row.Id,
            "deleted",
            ct
        );
        return Result.Success();
    }

    private Task<DiscordLiveRoleConfig?> FindAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct
    ) =>
        _db.DiscordLiveRoleConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId,
            ct
        );

    // One Discord role serves one purpose: a role viewers pick for pings is not also the role worn while live.
    private async Task<Result> EnsureRoleNotSelfAssignAsync(
        Guid connectionId,
        string roleId,
        CancellationToken ct
    )
    {
        string trimmed = roleId.Trim();
        bool pickedByViewers = await _db.DiscordNotificationRoles.AnyAsync(
            r => r.GuildConnectionId == connectionId && r.DiscordRoleId == trimmed,
            ct
        );

        return pickedByViewers
            ? Result.Failure(
                "Viewers already pick that Discord role for pings. Choose a different role for the live role.",
                "ROLE_IN_USE_AS_SELF_ASSIGN"
            )
            : Result.Success();
    }

    // Discord has no live-role member to pick: the account Discord links to the streamer's Twitch is used. Only the
    // installer's own Discord profile can say so, so a bot added without that link has nobody to give the role to.
    private static Result<T> NeedsTwitchLink<T>() =>
        Result.Failure<T>(
            "The bot does not know your Discord account yet. In Discord, open User Settings > Connections and "
                + "connect your Twitch account, then add the bot to your server again.",
            "LIVE_ROLE_NEEDS_TWITCH_LINK"
        );

    private static Result Validate(string roleId, string? discordMemberId)
    {
        if (string.IsNullOrWhiteSpace(roleId) || roleId.Trim().Length > MaxIdLength)
            return Errors.ValidationFailed(
                $"A Discord role id of 1 to {MaxIdLength} characters is required."
            );

        if (
            discordMemberId is not null
            && (
                string.IsNullOrWhiteSpace(discordMemberId)
                || discordMemberId.Trim().Length > MaxIdLength
            )
        )
            return Errors.ValidationFailed(
                $"A Discord member id of 1 to {MaxIdLength} characters is required."
            );

        return Result.Success();
    }

    private static DiscordLiveRoleConfigDto ToDto(DiscordLiveRoleConfig row) =>
        new(
            row.Id,
            row.GuildConnectionId,
            row.RoleId,
            row.DiscordMemberId,
            row.Enabled,
            row.IsCurrentlyApplied,
            row.CreatedAt,
            row.UpdatedAt
        );
}
