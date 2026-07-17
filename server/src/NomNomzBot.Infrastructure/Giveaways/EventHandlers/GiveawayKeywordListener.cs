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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Giveaways.EventHandlers;

/// <summary>
/// The giveaway chat reactions (giveaways.md §5/D7) — one generic listener over the canonical chat
/// fact, cross-platform by construction: (1) while a <c>keyword</c>-mode giveaway is OPEN, a message
/// that IS the keyword (trimmed, case-insensitive) enters the chatter through the full
/// <c>EnterAsync</c> path (eligibility, dedupe, cost, tickets — a rejected entry is simply silent in
/// chat); (2) while a claim window is armed, any message from a <c>drawn</c> winner marks their win
/// <c>claimed</c> — "responding" is exactly what the claim window asks for.
/// </summary>
public sealed class GiveawayKeywordListener(
    IServiceScopeFactory scopeFactory,
    IUserService userService,
    IChannelRegistry registry,
    ILogger<GiveawayKeywordListener> logger
) : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrWhiteSpace(@event.Message))
            return;

        // Bot-side standing (J.12): a muted/shadowbanned chatter cannot enter or claim giveaways.
        if (
            registry
                .Get(@event.BroadcasterId)
                ?.ModerationStandingFor(@event.Provider, @event.UserId)
            is not null
        )
            return;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Cheap pre-read: the single active giveaway row (indexed (BroadcasterId, Status)) decides in
        // one query whether this message matters at all — the hot chat path stays hot.
        Giveaway? active = await db
            .Giveaways.AsNoTracking()
            .FirstOrDefaultAsync(
                g =>
                    g.BroadcasterId == @event.BroadcasterId
                    && (g.Status == GiveawayStatus.Open || g.Status == GiveawayStatus.Drawn),
                cancellationToken
            );
        if (active is null)
            return;

        bool isKeywordEntry =
            active.Status == GiveawayStatus.Open
            && active.EntryMode == GiveawayEntryMode.Keyword
            && active.Keyword is { Length: > 0 } keyword
            && string.Equals(@event.Message.Trim(), keyword, StringComparison.OrdinalIgnoreCase);
        bool claimWindowArmed =
            active.Status == GiveawayStatus.Drawn && active.ClaimWindowMinutes is not null;
        if (!isKeywordEntry && !claimWindowArmed)
            return;

        Result<UserDto> user = await userService.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            cancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
            return;

        if (isKeywordEntry)
        {
            IGiveawayService giveaways =
                scope.ServiceProvider.GetRequiredService<IGiveawayService>();
            Result<GiveawayEntryDto> entered = await giveaways.EnterAsync(
                @event.BroadcasterId,
                active.Id,
                viewerUserId,
                cancellationToken
            );
            // A rejected entry (ineligible / duplicate / broke) is deliberately silent in chat —
            // spamming per-viewer rejections would drown the room while a giveaway runs.
            if (entered.IsFailure && entered.ErrorCode is not ("ALREADY_ENTERED" or "NOT_ELIGIBLE"))
                logger.LogDebug(
                    "Giveaway keyword entry failed for {UserId}: {Error} ({Code})",
                    viewerUserId,
                    entered.ErrorMessage,
                    entered.ErrorCode
                );
            return;
        }

        // Claim marking: any chat message from a drawn winner inside the window IS the response.
        GiveawayWinner? winner = await db.GiveawayWinners.FirstOrDefaultAsync(
            w =>
                w.GiveawayId == active.Id
                && w.ViewerUserId == viewerUserId
                && w.Status == GiveawayWinnerStatus.Drawn,
            cancellationToken
        );
        if (winner is null)
            return;

        winner.Status = GiveawayWinnerStatus.Claimed;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Giveaway winner {WinnerId} claimed by chatting within the window",
            winner.Id
        );
    }
}
