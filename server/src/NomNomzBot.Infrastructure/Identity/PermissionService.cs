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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Manages per-channel user permissions stored in the Permission entity.
///
/// SubjectType: "user" | "role"
/// ResourceType: "command" | "feature" | "channel" | "reward"
/// PermissionValue: "allow" | "deny" | "1" | "0"
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IApplicationDbContext _db;

    public PermissionService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<bool>> CheckPermissionAsync(
        string userId,
        string broadcasterId,
        string permission,
        CancellationToken cancellationToken = default
    )
    {
        // Broadcaster always has full access
        if (userId == broadcasterId)
            return Result.Success(true);

        // Check explicit user permission
        Permission? userPerm = await _db.Permissions.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.SubjectType == "user"
                && p.SubjectId == userId
                && p.ResourceType == "channel"
                && (p.ResourceId == permission || p.ResourceId == "*"),
            cancellationToken
        );

        if (userPerm is not null)
            return Result.Success(userPerm.PermissionValue is "allow" or "1");

        // Check moderator status — moderators have elevated permissions
        bool isModerator = await _db.ChannelModerators.AnyAsync(
            m => m.ChannelId == broadcasterId && m.UserId == userId,
            cancellationToken
        );

        if (isModerator)
        {
            // Moderators can manage most features except broadcaster-only ones
            string[] broadcasterOnly = new[]
            {
                "channel.delete",
                "channel.transfer",
                "bot.configure",
            };
            return Result.Success(!broadcasterOnly.Contains(permission));
        }

        // Default: deny
        return Result.Success(false);
    }

    public async Task<Result> GrantAsync(
        string broadcasterId,
        string userId,
        string permission,
        CancellationToken cancellationToken = default
    )
    {
        Permission? existing = await _db.Permissions.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.SubjectType == "user"
                && p.SubjectId == userId
                && p.ResourceType == "channel"
                && p.ResourceId == permission,
            cancellationToken
        );

        if (existing is not null)
        {
            existing.PermissionValue = "allow";
        }
        else
        {
            _db.Permissions.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    SubjectType = "user",
                    SubjectId = userId,
                    ResourceType = "channel",
                    ResourceId = permission,
                    PermissionValue = "allow",
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RevokeAsync(
        string broadcasterId,
        string userId,
        string permission,
        CancellationToken cancellationToken = default
    )
    {
        Permission? existing = await _db.Permissions.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.SubjectType == "user"
                && p.SubjectId == userId
                && p.ResourceType == "channel"
                && p.ResourceId == permission,
            cancellationToken
        );

        if (existing is null)
            return Result.Success(); // Nothing to revoke

        existing.PermissionValue = "deny";
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Broadcaster gets all permissions
        if (userId == broadcasterId)
        {
            return Result.Success<IReadOnlyList<string>>([
                "channel.*",
                "commands.*",
                "features.*",
                "rewards.*",
                "moderation.*",
            ]);
        }

        List<string> permissions = await _db
            .Permissions.Where(p =>
                p.BroadcasterId == broadcasterId
                && p.SubjectType == "user"
                && p.SubjectId == userId
                && p.PermissionValue == "allow"
            )
            .Select(p => p.ResourceId ?? p.ResourceType)
            .ToListAsync(cancellationToken);

        // Add moderator base permissions
        bool isModerator = await _db.ChannelModerators.AnyAsync(
            m => m.ChannelId == broadcasterId && m.UserId == userId,
            cancellationToken
        );

        if (isModerator)
        {
            permissions.AddRange([
                "moderation.timeout",
                "moderation.ban",
                "commands.execute",
                "features.view",
            ]);
        }

        return Result.Success<IReadOnlyList<string>>(permissions.Distinct().ToList());
    }

    public async Task<bool> HasChannelAccessAsync(
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Access = user is broadcaster, moderator, or has any explicit allow
        if (userId == broadcasterId)
            return true;

        bool isModerator = await _db.ChannelModerators.AnyAsync(
            m => m.ChannelId == broadcasterId && m.UserId == userId,
            cancellationToken
        );

        if (isModerator)
            return true;

        return await _db.Permissions.AnyAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.SubjectType == "user"
                && p.SubjectId == userId
                && p.PermissionValue == "allow",
            cancellationToken
        );
    }
}
