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

namespace NomNomzBot.Infrastructure.Discord;

/// <inheritdoc cref="IDiscordLiveRoleConfigService" />
public sealed class DiscordLiveRoleConfigService : IDiscordLiveRoleConfigService
{
    private const int MaxIdLength = 50;

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public DiscordLiveRoleConfigService(IApplicationDbContext db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
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

        bool connectionExists = await _db.DiscordGuildConnections.AnyAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );
        if (!connectionExists)
            return Errors.NotFound<DiscordLiveRoleConfigDto>(
                "Discord connection",
                connectionId.ToString()
            );

        bool exists = await _db.DiscordLiveRoleConfigs.AnyAsync(
            c => c.BroadcasterId == broadcasterId && c.GuildConnectionId == connectionId,
            ct
        );
        if (exists)
            return Result.Failure<DiscordLiveRoleConfigDto>(
                "A live-role rule already exists for this connection.",
                "ALREADY_EXISTS"
            );

        DiscordLiveRoleConfig row = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            GuildConnectionId = connectionId,
            RoleId = request.RoleId.Trim(),
            DiscordMemberId = request.DiscordMemberId.Trim(),
            Enabled = request.Enabled,
        };
        _db.DiscordLiveRoleConfigs.Add(row);
        await _unitOfWork.SaveChangesAsync(ct);

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
        string memberId = request.DiscordMemberId.Trim();
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

    private static Result Validate(string roleId, string discordMemberId)
    {
        if (string.IsNullOrWhiteSpace(roleId) || roleId.Trim().Length > MaxIdLength)
            return Errors.ValidationFailed(
                $"A Discord role id of 1 to {MaxIdLength} characters is required."
            );

        if (
            string.IsNullOrWhiteSpace(discordMemberId)
            || discordMemberId.Trim().Length > MaxIdLength
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
