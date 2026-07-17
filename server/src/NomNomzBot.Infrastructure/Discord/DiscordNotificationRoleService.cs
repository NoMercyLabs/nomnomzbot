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
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Self-assign notify roles + member opt-in management (discord.md §3.3). The button is posted and opt-in/out
/// roles are pushed into the guild through <see cref="IDiscordBotGateway"/>; opt-in changes publish
/// <see cref="DiscordMemberOptInChangedEvent"/>.
/// </summary>
public sealed class DiscordNotificationRoleService : IDiscordNotificationRoleService
{
    private const string ButtonLabel = "Notify me";

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDiscordBotGateway _gateway;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public DiscordNotificationRoleService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IDiscordBotGateway gateway,
        IEventBus eventBus,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _gateway = gateway;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<Result<IReadOnlyList<DiscordNotificationRoleDto>>> GetRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        List<DiscordNotificationRole> roles = await _db
            .DiscordNotificationRoles.Where(r =>
                r.BroadcasterId == broadcasterId && r.GuildConnectionId == connectionId
            )
            .OrderBy(r => r.RoleName)
            .ToListAsync(ct);

        // Live opt-in counts (active opt-ins = OptedOutAt is null) per role, in one grouped query.
        List<Guid> roleIds = [.. roles.Select(r => r.Id)];
        Dictionary<Guid, int> counts = await _db
            .DiscordMemberOptIns.Where(o =>
                roleIds.Contains(o.NotificationRoleId) && o.OptedOutAt == null
            )
            .GroupBy(o => o.NotificationRoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        IReadOnlyList<DiscordNotificationRoleDto> dtos =
        [
            .. roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))),
        ];
        return Result.Success(dtos);
    }

    public async Task<Result<DiscordNotificationRoleDto>> CreateRoleAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordNotificationRoleRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.DiscordRoleId))
            return Result.Failure<DiscordNotificationRoleDto>(
                "A Discord role id is required.",
                "VALIDATION_FAILED"
            );

        bool connectionExists = await _db.DiscordGuildConnections.AnyAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );
        if (!connectionExists)
            return Errors.NotFound<DiscordNotificationRoleDto>(
                "Discord connection",
                connectionId.ToString()
            );

        bool exists = await _db.DiscordNotificationRoles.AnyAsync(
            r => r.GuildConnectionId == connectionId && r.DiscordRoleId == request.DiscordRoleId,
            ct
        );
        if (exists)
            return Result.Failure<DiscordNotificationRoleDto>(
                "That Discord role is already registered for this connection.",
                "ALREADY_EXISTS"
            );

        DiscordNotificationRole role = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            GuildConnectionId = connectionId,
            DiscordRoleId = request.DiscordRoleId.Trim(),
            RoleName = request.RoleName?.Trim(),
            SelfAssignEnabled = request.SelfAssignEnabled,
            DmEnabled = request.DmEnabled,
        };

        _db.DiscordNotificationRoles.Add(role);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToDto(role, 0));
    }

    public async Task<Result<DiscordNotificationRoleDto>> UpdateRoleAsync(
        Guid broadcasterId,
        Guid roleId,
        UpdateDiscordNotificationRoleRequest request,
        CancellationToken ct = default
    )
    {
        DiscordNotificationRole? role = await FindAsync(broadcasterId, roleId, ct);
        if (role is null)
            return Errors.NotFound<DiscordNotificationRoleDto>("Discord role", roleId.ToString());

        role.RoleName = request.RoleName?.Trim();
        role.SelfAssignEnabled = request.SelfAssignEnabled;
        role.DmEnabled = request.DmEnabled;
        await _db.SaveChangesAsync(ct);

        int count = await ActiveOptInCountAsync(roleId, ct);
        return Result.Success(ToDto(role, count));
    }

    public async Task<Result> DeleteRoleAsync(
        Guid broadcasterId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        DiscordNotificationRole? role = await FindAsync(broadcasterId, roleId, ct);
        if (role is null)
            return Errors.NotFound<object>("Discord role", roleId.ToString());

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Null the FK on any config that pinged this role (FK is nullable) — same transaction.
            List<DiscordNotificationConfig> referencing = await _db
                .DiscordNotificationConfigs.Where(c => c.PingRoleId == roleId)
                .ToListAsync(ct);
            foreach (DiscordNotificationConfig config in referencing)
                config.PingRoleId = null;

            _db.DiscordNotificationRoles.Remove(role); // soft-delete via interceptor
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        return Result.Success();
    }

    public async Task<Result<DiscordNotificationRoleDto>> PostOptInButtonAsync(
        Guid broadcasterId,
        Guid roleId,
        string buttonChannelId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(buttonChannelId))
            return Result.Failure<DiscordNotificationRoleDto>(
                "A button channel id is required.",
                "VALIDATION_FAILED"
            );

        DiscordNotificationRole? role = await FindAsync(broadcasterId, roleId, ct);
        if (role is null)
            return Errors.NotFound<DiscordNotificationRoleDto>("Discord role", roleId.ToString());

        string roleLabel = role.RoleName is null ? ButtonLabel : $"Notify me — {role.RoleName}";
        Result<string> posted = await _gateway.PostButtonMessageAsync(
            broadcasterId,
            buttonChannelId,
            new DiscordOptInButton(
                $"Click to toggle the **{role.RoleName ?? "notify"}** role.",
                role.Id,
                roleLabel
            ),
            ct
        );
        if (posted.IsFailure)
            return Result.Failure<DiscordNotificationRoleDto>(
                posted.ErrorMessage,
                posted.ErrorCode
            );

        role.ButtonChannelId = buttonChannelId.Trim();
        role.ButtonMessageId = posted.Value;
        await _db.SaveChangesAsync(ct);

        int count = await ActiveOptInCountAsync(roleId, ct);
        return Result.Success(ToDto(role, count));
    }

    public async Task<Result> OptInMemberAsync(
        Guid broadcasterId,
        Guid roleId,
        string discordMemberId,
        string source,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(discordMemberId))
            return Errors.ValidationFailed("A Discord member id is required.");

        DiscordNotificationRole? role = await FindAsync(broadcasterId, roleId, ct);
        if (role is null)
            return Errors.NotFound<object>("Discord role", roleId.ToString());

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        DiscordMemberOptIn? optIn = await _db.DiscordMemberOptIns.FirstOrDefaultAsync(
            o => o.NotificationRoleId == roleId && o.DiscordMemberId == discordMemberId,
            ct
        );

        if (optIn is null)
        {
            optIn = new DiscordMemberOptIn
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                NotificationRoleId = roleId,
                DiscordMemberId = discordMemberId,
                OptInSource = source,
                OptedInAt = now,
            };
            _db.DiscordMemberOptIns.Add(optIn);
        }
        else
        {
            optIn.OptInSource = source;
            optIn.OptedInAt = now;
            optIn.OptedOutAt = null;
        }

        await _db.SaveChangesAsync(ct);

        // Push the role into the guild (best-effort — the opt-in is recorded regardless).
        Result roleAdded = await _gateway.AddMemberRoleAsync(
            broadcasterId,
            role.GuildConnection?.GuildId ?? await GuildIdAsync(role.GuildConnectionId, ct),
            discordMemberId,
            role.DiscordRoleId,
            ct
        );
        if (roleAdded.IsFailure)
            return roleAdded;

        await _eventBus.PublishAsync(
            new DiscordMemberOptInChangedEvent
            {
                BroadcasterId = broadcasterId,
                NotificationRoleId = roleId,
                DiscordMemberId = discordMemberId,
                OptedIn = true,
                Source = source,
            },
            ct
        );

        return Result.Success();
    }

    public async Task<Result> OptOutMemberAsync(
        Guid broadcasterId,
        Guid roleId,
        string discordMemberId,
        string source,
        CancellationToken ct = default
    )
    {
        DiscordNotificationRole? role = await FindAsync(broadcasterId, roleId, ct);
        if (role is null)
            return Errors.NotFound<object>("Discord role", roleId.ToString());

        DiscordMemberOptIn? optIn = await _db.DiscordMemberOptIns.FirstOrDefaultAsync(
            o => o.NotificationRoleId == roleId && o.DiscordMemberId == discordMemberId,
            ct
        );
        if (optIn is null)
            return Errors.NotFound<object>("Discord opt-in", discordMemberId);

        optIn.OptedOutAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        Result roleRemoved = await _gateway.RemoveMemberRoleAsync(
            broadcasterId,
            role.GuildConnection?.GuildId ?? await GuildIdAsync(role.GuildConnectionId, ct),
            discordMemberId,
            role.DiscordRoleId,
            ct
        );
        if (roleRemoved.IsFailure)
            return roleRemoved;

        await _eventBus.PublishAsync(
            new DiscordMemberOptInChangedEvent
            {
                BroadcasterId = broadcasterId,
                NotificationRoleId = roleId,
                DiscordMemberId = discordMemberId,
                OptedIn = false,
                Source = source,
            },
            ct
        );

        return Result.Success();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private Task<DiscordNotificationRole?> FindAsync(
        Guid broadcasterId,
        Guid roleId,
        CancellationToken ct
    ) =>
        _db.DiscordNotificationRoles.FirstOrDefaultAsync(
            r => r.Id == roleId && r.BroadcasterId == broadcasterId,
            ct
        );

    private Task<int> ActiveOptInCountAsync(Guid roleId, CancellationToken ct) =>
        _db.DiscordMemberOptIns.CountAsync(
            o => o.NotificationRoleId == roleId && o.OptedOutAt == null,
            ct
        );

    private async Task<string> GuildIdAsync(Guid connectionId, CancellationToken ct) =>
        await _db
            .DiscordGuildConnections.Where(c => c.Id == connectionId)
            .Select(c => c.GuildId)
            .FirstOrDefaultAsync(ct)
        ?? string.Empty;

    private static DiscordNotificationRoleDto ToDto(DiscordNotificationRole r, int optInCount) =>
        new(
            r.Id,
            r.GuildConnectionId,
            r.DiscordRoleId,
            r.RoleName,
            r.SelfAssignEnabled,
            r.DmEnabled,
            r.ButtonMessageId,
            r.ButtonChannelId,
            optInCount,
            r.CreatedAt,
            r.UpdatedAt
        );
}
