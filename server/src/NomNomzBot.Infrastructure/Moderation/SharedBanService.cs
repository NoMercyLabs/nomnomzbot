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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The shared-chat ban trust web (moderation.md §3.5, schema J.9/J.9a). Reads are plain; every WRITE
/// re-verifies the SuperMod floor in-process (<see cref="IRoleResolver"/> effective level ≥ LeadModerator(20))
/// — defense in depth beside the Gate-2 <c>moderation:sharedban:write</c> policy, never the gate alone.
/// A channel with no settings row reads as the safe defaults: accept OFF, share OFF, empty trust list.
/// </summary>
public sealed class SharedBanService(IApplicationDbContext db, IRoleResolver roles)
    : ISharedBanService
{
    private static readonly int SuperModFloor = ManagementRole.LeadModerator.ToLevel();

    public async Task<Result<SharedBanSettingsDto>> GetSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SharedBanSettings? settings = await db.SharedBanSettings.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId,
            ct
        );
        return Result.Success(
            new SharedBanSettingsDto(
                settings?.AcceptSharedChatBans ?? false,
                settings?.ShareOutgoingBans ?? false,
                await TrustedListAsync(broadcasterId, ct)
            )
        );
    }

    public async Task<Result<SharedBanSettingsDto>> SaveSettingsAsync(
        Guid broadcasterId,
        Guid actorUserId,
        SaveSharedBanSettingsRequest request,
        CancellationToken ct = default
    )
    {
        Result floor = await RequireSuperModAsync(broadcasterId, actorUserId, ct);
        if (floor.IsFailure)
            return floor.WithValue<SharedBanSettingsDto>(null!);

        SharedBanSettings? settings = await db.SharedBanSettings.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId,
            ct
        );
        if (settings is null)
        {
            settings = new SharedBanSettings { BroadcasterId = broadcasterId };
            db.SharedBanSettings.Add(settings);
        }
        settings.AcceptSharedChatBans = request.AcceptSharedChatBans;
        settings.ShareOutgoingBans = request.ShareOutgoingBans;
        await db.SaveChangesAsync(ct);

        return Result.Success(
            new SharedBanSettingsDto(
                settings.AcceptSharedChatBans,
                settings.ShareOutgoingBans,
                await TrustedListAsync(broadcasterId, ct)
            )
        );
    }

    public async Task<Result<SharedBanTrustedChannelDto>> AddTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    )
    {
        Result floor = await RequireSuperModAsync(broadcasterId, actorUserId, ct);
        if (floor.IsFailure)
            return floor.WithValue<SharedBanTrustedChannelDto>(null!);

        if (trustedChannelId == broadcasterId)
            return Result.Failure<SharedBanTrustedChannelDto>(
                "A channel cannot trust itself.",
                "VALIDATION_FAILED"
            );
        if (!await db.Channels.AnyAsync(c => c.Id == trustedChannelId, ct))
            return Result.Failure<SharedBanTrustedChannelDto>("Unknown channel.", "NOT_FOUND");

        // Idempotent add: re-trusting an existing partner returns the existing row unchanged.
        SharedBanTrustedChannel? existing = await db.SharedBanTrustedChannels.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.TrustedChannelId == trustedChannelId,
            ct
        );
        if (existing is null)
        {
            existing = new SharedBanTrustedChannel
            {
                BroadcasterId = broadcasterId,
                TrustedChannelId = trustedChannelId,
                AddedByUserId = actorUserId,
            };
            db.SharedBanTrustedChannels.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        string name =
            await db
                .Channels.Where(c => c.Id == trustedChannelId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct)
            ?? "";
        return Result.Success(
            new SharedBanTrustedChannelDto(
                existing.TrustedChannelId,
                name,
                existing.AddedByUserId,
                existing.CreatedAt
            )
        );
    }

    public async Task<Result> RemoveTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    )
    {
        Result floor = await RequireSuperModAsync(broadcasterId, actorUserId, ct);
        if (floor.IsFailure)
            return floor;

        SharedBanTrustedChannel? entry = await db.SharedBanTrustedChannels.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.TrustedChannelId == trustedChannelId,
            ct
        );
        if (entry is null)
            return Result.Failure("That channel is not on the trust list.", "NOT_FOUND");

        db.SharedBanTrustedChannels.Remove(entry);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>The in-process SuperMod floor re-check (§5 footnote: never trust the gate alone).</summary>
    private async Task<Result> RequireSuperModAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CancellationToken ct
    )
    {
        Result<int> level = await roles.ResolveEffectiveLevelAsync(actorUserId, broadcasterId, ct);
        if (level.IsFailure)
            return level;
        return level.Value >= SuperModFloor
            ? Result.Success()
            : Result.Failure(
                "Shared-ban settings require the SuperMod tier or above.",
                "FORBIDDEN"
            );
    }

    private async Task<IReadOnlyList<SharedBanTrustedChannelDto>> TrustedListAsync(
        Guid broadcasterId,
        CancellationToken ct
    ) =>
        await db
            .SharedBanTrustedChannels.Where(t => t.BroadcasterId == broadcasterId)
            .Join(
                db.Channels,
                t => t.TrustedChannelId,
                c => c.Id,
                (t, c) =>
                    new SharedBanTrustedChannelDto(
                        t.TrustedChannelId,
                        c.Name,
                        t.AddedByUserId,
                        t.CreatedAt
                    )
            )
            .OrderBy(dto => dto.CreatedAt)
            .ToListAsync(ct);
}
