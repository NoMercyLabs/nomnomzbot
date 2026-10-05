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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Enforces a reward's <see cref="Reward.Permission"/> floor on a redemption. A viewer below it gets the
/// redemption refunded (Twitch status CANCELED, so the points return) and one chat line; the caller then runs
/// nothing. The viewer's standing is the same effective level the chat command gate uses
/// (<see cref="IRoleResolver"/>, the unified ladder), compared against the floor named by
/// <see cref="ChatRole.Parse"/>. A viewer the resolver cannot place counts as Everyone, so the gate fails closed.
/// </summary>
internal static class RewardPermissionGate
{
    /// <summary>
    /// Returns true when the redemption was refused (the caller must stop). Returns false when the viewer
    /// meets the reward's permission, which includes every reward left at the default "everyone".
    /// </summary>
    public static async Task<bool> TryRefuseAsync(
        IServiceProvider services,
        IApplicationDbContext db,
        Reward reward,
        RewardRedeemedEvent redemption,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        int floor = ChatRole.Parse(reward.Permission).ToLevelValue();
        if (floor <= PermissionLevel.Everyone.ToLevelValue())
            return false;

        int viewerLevel = await ResolveViewerLevelAsync(
            services,
            db,
            redemption,
            cancellationToken
        );
        if (viewerLevel >= floor)
            return false;

        await RefundAsync(services, db, redemption, logger, cancellationToken);
        return true;
    }

    private static async Task<int> ResolveViewerLevelAsync(
        IServiceProvider services,
        IApplicationDbContext db,
        RewardRedeemedEvent redemption,
        CancellationToken ct
    )
    {
        Guid? viewerUserId = await db
            .Users.Where(u => u.TwitchUserId == redemption.UserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (viewerUserId is null)
            return PermissionLevel.Everyone.ToLevelValue();

        IRoleResolver roles = services.GetRequiredService<IRoleResolver>();
        Result<int> resolved = await roles.ResolveEffectiveLevelAsync(
            viewerUserId.Value,
            redemption.BroadcasterId,
            ct
        );
        return resolved.IsSuccess ? resolved.Value : PermissionLevel.Everyone.ToLevelValue();
    }

    private static async Task RefundAsync(
        IServiceProvider services,
        IApplicationDbContext db,
        RewardRedeemedEvent redemption,
        ILogger logger,
        CancellationToken ct
    )
    {
        IRewardService rewards = services.GetRequiredService<IRewardService>();
        Result refund = await rewards.SetRedemptionStatusAsync(
            redemption.BroadcasterId.ToString(),
            redemption.RedemptionId,
            "CANCELED",
            redemption.RewardId,
            ct
        );
        if (refund.IsFailure)
        {
            // No chat line: it promises a refund that did not happen.
            logger.LogWarning(
                "Refund of redemption {RedemptionId} in {Channel} failed: {Error}",
                redemption.RedemptionId,
                redemption.BroadcasterId,
                refund.ErrorMessage
            );
            return;
        }

        string personality =
            await db
                .Channels.Where(c => c.Id == redemption.BroadcasterId)
                .Select(c => c.Personality)
                .FirstOrDefaultAsync(ct)
            ?? PersonalityTone.Informative;
        IBuiltinResponseComposer composer = services.GetRequiredService<IBuiltinResponseComposer>();
        string message = await composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = redemption.BroadcasterId,
                Personality = personality,
                BuiltinKey = BuiltinResponseSlots.Reward.Key,
                Slot = BuiltinResponseSlots.Reward.NoPermission,
                NeutralFallback =
                    ToneTemplateCatalog.ShippedTemplate(
                        BuiltinResponseSlots.Reward.Key,
                        BuiltinResponseSlots.Reward.NoPermission
                    ) ?? string.Empty,
                Variables = new Dictionary<string, string>
                {
                    ["user"] = redemption.UserDisplayName,
                },
            },
            ct
        );
        if (message.Length == 0)
            return;

        IChatProvider chat = services.GetRequiredService<IChatProvider>();
        if (!await chat.SendMessageAsync(redemption.BroadcasterId, message, ct))
            logger.LogWarning(
                "Refusal chat line for redemption {RedemptionId} in {Channel} was not accepted",
                redemption.RedemptionId,
                redemption.BroadcasterId
            );
    }
}
