// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <inheritdoc cref="IOwnNameSpamRemovalService" />
public sealed class OwnNameSpamRemovalService(IApplicationDbContext db) : IOwnNameSpamRemovalService
{
    public async Task<OwnNameSpamRemovalDto> GetAsync(
        Guid broadcasterId,
        Guid moderatorUserId,
        CancellationToken ct = default
    )
    {
        ChannelMembership? membership = await MembershipAsync(broadcasterId, moderatorUserId, ct);
        if (membership is null || await IsOnboardedAsync(broadcasterId, ct))
            return new OwnNameSpamRemovalDto(Enabled: false, Applies: false);

        return new OwnNameSpamRemovalDto(membership.RemovesSpamInOwnName, Applies: true);
    }

    public async Task<Result<OwnNameSpamRemovalDto>> SetAsync(
        Guid broadcasterId,
        Guid moderatorUserId,
        bool enabled,
        CancellationToken ct = default
    )
    {
        ChannelMembership? membership = await MembershipAsync(broadcasterId, moderatorUserId, ct);
        if (membership is null)
            return Result.Failure<OwnNameSpamRemovalDto>(
                "You hold no moderator role on this channel.",
                "FORBIDDEN"
            );

        if (await IsOnboardedAsync(broadcasterId, ct))
            return Result.Failure<OwnNameSpamRemovalDto>(
                "This channel uses the bot itself, so spam is removed with the channel's own access.",
                "VALIDATION_FAILED"
            );

        membership.RemovesSpamInOwnName = enabled;
        await db.SaveChangesAsync(ct);
        return Result.Success(new OwnNameSpamRemovalDto(enabled, Applies: true));
    }

    // IgnoreQueryFilters: the membership belongs to the channel being moderated, not the request's tenant.
    private Task<ChannelMembership?> MembershipAsync(
        Guid broadcasterId,
        Guid moderatorUserId,
        CancellationToken ct
    ) =>
        db
            .ChannelMemberships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                m =>
                    m.BroadcasterId == broadcasterId
                    && m.UserId == moderatorUserId
                    && m.DeletedAt == null,
                ct
            );

    private Task<bool> IsOnboardedAsync(Guid broadcasterId, CancellationToken ct) =>
        db.Channels.IgnoreQueryFilters().AnyAsync(c => c.Id == broadcasterId && c.IsOnboarded, ct);
}
