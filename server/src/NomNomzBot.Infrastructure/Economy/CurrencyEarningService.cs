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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Applies earning rules to engagement events (economy.md §3.3) — credits currency through the ledger core.
/// Implements rule resolution, the role-level gate, idempotency per <c>(source, EventId)</c>, and the rolling
/// per-window cap (folded from the ledger). The per-stream cap, BonusConfig multipliers, and anti-AFK presence
/// (beyond the caller-supplied <c>PresenceVerified</c>) are deferred — they depend on the per-stream earned
/// cache, the bonus-multiplier schema, and the presence subsystem respectively.
/// </summary>
public sealed class CurrencyEarningService(
    IApplicationDbContext db,
    ICurrencyAccountService accounts,
    IEventBus eventBus,
    TimeProvider clock
) : ICurrencyEarningService
{
    public async Task<Result<long>> ApplyEarningAsync(
        Guid broadcasterId,
        EarnRequest request,
        CancellationToken ct = default
    )
    {
        if (!Enum.TryParse(request.Source, ignoreCase: true, out EarningSource source))
            return Result.Success(0L); // unknown source → no-op

        EarningRule? rule = await db.EarningRules.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == broadcasterId
                && r.Source == source
                && r.IsEnabled
                && r.DeletedAt == null,
            ct
        );
        if (rule is null)
            return Result.Success(0L); // missing/disabled → no-op

        if (rule.MinRoleLevel is int minLevel)
        {
            int level =
                request.ViewerRoleLevel
                ?? await ResolveStandingLevelAsync(broadcasterId, request.ViewerUserId, ct);
            if (level < minLevel)
                return Result.Success(0L);
        }

        CurrencyEntryType earnType = EarnEntryType(source);

        // Idempotent per (source, EventId): an EventSub-sourced earn is applied at most once.
        if (
            request.EventId is Guid eventId
            && await db.CurrencyLedgerEntries.AnyAsync(
                e =>
                    e.BroadcasterId == broadcasterId
                    && e.ViewerUserId == request.ViewerUserId
                    && e.EventId == eventId
                    && e.EntryType == earnType,
                ct
            )
        )
            return Result.Success(0L);

        long amount = rule.Rate * request.Units;
        if (amount <= 0)
            return Result.Success(0L);

        bool capped = false;
        if (rule.PerWindowCap is long windowCap && rule.UnitWindowSeconds is int windowSeconds)
        {
            DateTime since = clock.GetUtcNow().UtcDateTime.AddSeconds(-windowSeconds);
            long earnedInWindow = await db
                .CurrencyLedgerEntries.Where(e =>
                    e.BroadcasterId == broadcasterId
                    && e.ViewerUserId == request.ViewerUserId
                    && e.EntryType == earnType
                    && e.CreatedAt >= since
                )
                .SumAsync(e => e.Amount, ct);
            long remaining = windowCap - earnedInWindow;
            if (remaining <= 0)
                return Result.Success(0L);
            if (amount > remaining)
            {
                amount = remaining;
                capped = true;
            }
        }

        Result<CurrencyLedgerEntryDto> posted = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                request.ViewerUserId,
                amount,
                earnType.ToString(),
                nameof(CurrencyLedgerSourceType.EarningRule),
                rule.Id,
                request.EventId,
                Reason: null,
                ActorUserId: null,
                IdempotencyKey: null
            ),
            ct
        );
        if (posted.IsFailure)
            return Result.Success(0L); // can't credit (disabled/frozen/at cap) → earned nothing

        await eventBus.PublishAsync(
            new CurrencyEarnedEvent
            {
                BroadcasterId = broadcasterId,
                AccountId = posted.Value.AccountId,
                ViewerUserId = request.ViewerUserId,
                Source = source.ToString(),
                Amount = amount,
                Capped = capped,
            },
            ct
        );
        return Result.Success(amount);
    }

    public async Task<Result<IReadOnlyList<EarnResultDto>>> ApplyWatchTimeBatchAsync(
        Guid broadcasterId,
        WatchTimeBatchRequest request,
        CancellationToken ct = default
    )
    {
        List<EarnResultDto> results = [];
        foreach (WatchTimeViewer viewer in request.Viewers)
        {
            long units =
                viewer.PresenceVerified && request.WindowSeconds > 0
                    ? viewer.PresentSeconds / request.WindowSeconds
                    : 0;
            if (units <= 0)
            {
                results.Add(new EarnResultDto(viewer.ViewerUserId, 0, false));
                continue;
            }

            Result<long> credited = await ApplyEarningAsync(
                broadcasterId,
                new EarnRequest(
                    viewer.ViewerUserId,
                    nameof(EarningSource.WatchTime),
                    units,
                    EventId: null,
                    viewer.RoleLevel,
                    Context: null
                ),
                ct
            );
            results.Add(
                new EarnResultDto(
                    viewer.ViewerUserId,
                    credited.IsSuccess ? credited.Value : 0,
                    false
                )
            );
        }
        return Result.Success<IReadOnlyList<EarnResultDto>>(results);
    }

    private async Task<int> ResolveStandingLevelAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct
    ) =>
        await db
            .ChannelCommunityStandings.Where(s =>
                s.BroadcasterId == broadcasterId && s.UserId == viewerUserId
            )
            .Select(s => s.LevelValue)
            .FirstOrDefaultAsync(ct);

    private static CurrencyEntryType EarnEntryType(EarningSource source) =>
        source switch
        {
            EarningSource.ChatMessage => CurrencyEntryType.EarnChat,
            EarningSource.WatchTime => CurrencyEntryType.EarnWatchTime,
            EarningSource.Follow => CurrencyEntryType.EarnFollow,
            EarningSource.Subscription => CurrencyEntryType.EarnSubscription,
            EarningSource.GiftSubscription => CurrencyEntryType.EarnGiftSubscription,
            EarningSource.Cheer => CurrencyEntryType.EarnCheer,
            EarningSource.Raid => CurrencyEntryType.EarnRaid,
            _ => CurrencyEntryType.EarnChat,
        };
}
