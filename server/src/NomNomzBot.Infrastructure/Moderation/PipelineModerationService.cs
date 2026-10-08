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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Bans and timeouts issued by a pipeline (no signed-in moderator behind them). The channel's own platform
/// does the enforcing and the same <c>moderation_action</c> row a dashboard action writes follows it, so the
/// action log and the banned-viewers list show pipeline actions too.
/// <list type="bullet">
/// <item>Twitch tenant: <see cref="IModerationService"/> (Helix, signed as the channel owner — the operator for
/// channel automation — then records the row).</item>
/// <item>Any other tenant (Kick, YouTube): <see cref="IChatProvider"/> routes to the tenant's own platform;
/// the row is written only when that platform accepted the call.</item>
/// </list>
/// </summary>
public sealed class PipelineModerationService(
    IApplicationDbContext db,
    IModerationService moderation,
    IChatProvider chat
) : IPipelineModerationService
{
    private sealed record Tenant(string Provider, Guid OwnerUserId);

    public async Task<Result<ModerationActionResult>> BanAsync(
        Guid broadcasterId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        Tenant? tenant = await FindTenantAsync(broadcasterId, cancellationToken);
        if (tenant is null)
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId.ToString());

        if (tenant.Provider != AuthEnums.Platform.Twitch)
        {
            bool banned = await chat.BanUserAsync(
                broadcasterId,
                targetUserId,
                reason,
                cancellationToken
            );
            return banned
                ? await ModerationActionRecord.WriteAsync(
                    db,
                    broadcasterId,
                    "ban",
                    targetUserId,
                    reason,
                    durationSeconds: null,
                    moderatorId: null,
                    cancellationToken
                )
                : PlatformRefused("ban", tenant.Provider);
        }

        if (tenant.OwnerUserId == Guid.Empty)
            return NoOperator();

        return await moderation.BanAsync(
            broadcasterId.ToString(),
            tenant.OwnerUserId,
            targetUserId,
            reason,
            moderatorId: null,
            cancellationToken
        );
    }

    public async Task<Result<ModerationActionResult>> TimeoutAsync(
        Guid broadcasterId,
        string targetUserId,
        int durationSeconds,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        Tenant? tenant = await FindTenantAsync(broadcasterId, cancellationToken);
        if (tenant is null)
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId.ToString());

        if (tenant.Provider != AuthEnums.Platform.Twitch)
        {
            bool timedOut = await chat.TimeoutUserAsync(
                broadcasterId,
                targetUserId,
                durationSeconds,
                reason,
                cancellationToken
            );
            return timedOut
                ? await ModerationActionRecord.WriteAsync(
                    db,
                    broadcasterId,
                    "timeout",
                    targetUserId,
                    reason,
                    durationSeconds,
                    moderatorId: null,
                    cancellationToken
                )
                : PlatformRefused("timeout", tenant.Provider);
        }

        if (tenant.OwnerUserId == Guid.Empty)
            return NoOperator();

        return await moderation.TimeoutAsync(
            broadcasterId.ToString(),
            tenant.OwnerUserId,
            targetUserId,
            durationSeconds,
            reason,
            moderatorId: null,
            cancellationToken
        );
    }

    private async Task<Tenant?> FindTenantAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new Tenant(c.Provider, c.OwnerUserId))
            .FirstOrDefaultAsync(cancellationToken);

    private static Result<ModerationActionResult> PlatformRefused(string action, string provider) =>
        Result.Failure<ModerationActionResult>(
            $"The {provider} platform did not apply the {action}.",
            "PLATFORM_REJECTED"
        );

    private static Result<ModerationActionResult> NoOperator() =>
        Result.Failure<ModerationActionResult>(
            "The channel has no owner account to sign the moderation action with.",
            "NO_OPERATOR"
        );
}
