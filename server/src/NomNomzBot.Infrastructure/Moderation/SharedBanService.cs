// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The shared-chat ban trust web (moderation.md §3.5, schema J.9/J.9a). Reads are plain; every WRITE
/// re-verifies the SuperMod floor in-process (<see cref="IRoleResolver"/> effective level ≥ LeadModerator(20))
/// — defense in depth beside the Gate-2 <c>moderation:sharedban:write</c> policy, never the gate alone.
/// A channel with no settings row reads as the safe defaults: accept OFF, share OFF, empty trust list.
/// The inbound apply enforces the full trust predicate here (accept + trusted origin + same live session).
/// </summary>
public sealed class SharedBanService(
    IApplicationDbContext db,
    IRoleResolver roles,
    ISharedChatSessionTracker sessions,
    ITwitchModerationApi twitchModeration
) : ISharedBanService
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

    public async Task<Result<SharedBanApplicationResult>> ApplyInboundSharedBanAsync(
        Guid partnerBroadcasterId,
        SharedChatBanIssuedEvent inbound,
        CancellationToken ct = default
    )
    {
        // The double opt-in predicate — enforced HERE, never assumed from the caller.
        SharedBanSettings? settings = await db.SharedBanSettings.FirstOrDefaultAsync(
            s => s.BroadcasterId == partnerBroadcasterId,
            ct
        );
        if (settings is not { AcceptSharedChatBans: true })
            return Skipped("not_accepting");

        bool trusted = await db.SharedBanTrustedChannels.AnyAsync(
            t =>
                t.BroadcasterId == partnerBroadcasterId
                && t.TrustedChannelId == inbound.OriginChannelId,
            ct
        );
        if (!trusted)
            return Skipped("origin_not_trusted");

        // The session verification: the partner must be LIVE in the SAME shared-chat session.
        SharedChatSessionInfo? session = sessions.GetActiveSession(partnerBroadcasterId);
        if (session is null || session.SessionId != inbound.SharedChatSessionId)
            return Skipped("no_shared_session");

        // Ban on the partner's OWN tenant token (broadcaster = moderator = the channel) — system-
        // initiated, no operator involved.
        Result<TwitchBanResult> banned = await twitchModeration.BanUserAsync(
            partnerBroadcasterId,
            inbound.TargetTwitchUserId,
            inbound.Reason ?? $"Shared-chat ban from a trusted partner channel.",
            ct
        );
        if (banned.IsFailure)
            return Skipped($"twitch_ban_failed:{banned.ErrorCode}");

        // The provenance row: same RecordType + JSON shape ModerationService writes, plus the
        // federation origin fields (moderation.md §3.5) — the mod log shows WHERE the ban came from.
        Domain.Platform.Entities.Record record = new()
        {
            BroadcasterId = partnerBroadcasterId,
            RecordType = "moderation_action",
            Data = JsonSerializer.Serialize(
                new SharedBanActionData
                {
                    Action = "ban",
                    TargetUserId = inbound.TargetTwitchUserId,
                    TargetUsername = inbound.TargetDisplayName,
                    Reason = inbound.Reason,
                    Origin = "federation",
                    OriginChannelId = inbound.OriginChannelId,
                }
            ),
            UserId = inbound.OriginChannelId.ToString(), // the origin channel is the actor of record
        };
        db.Records.Add(record);
        await db.SaveChangesAsync(ct);

        return Result.Success(new SharedBanApplicationResult(true, null, record.Id));

        static Result<SharedBanApplicationResult> Skipped(string reason) =>
            Result.Success(new SharedBanApplicationResult(false, reason, null));
    }

    /// <summary>The recorded shape — a superset of ModerationService's action data (same JSON reader).</summary>
    private sealed class SharedBanActionData
    {
        public string Action { get; set; } = null!;
        public string TargetUserId { get; set; } = null!;
        public string? TargetUsername { get; set; }
        public string? Reason { get; set; }
        public string? Origin { get; set; }
        public Guid? OriginChannelId { get; set; }
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
