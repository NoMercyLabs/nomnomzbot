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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Folds <c>CurrencyCreditedEvent</c> and <c>CurrencyDebitedEvent</c> into <see cref="CurrencyAccount.Balance"/>,
/// <see cref="CurrencyAccount.LifetimeEarned"/>, and <see cref="CurrencyAccount.LifetimeSpent"/>.
///
/// Unlike most projections this one is NOT fully replay-safe on the Balance column alone — the
/// <c>BalanceAfter</c> value on the event is the authoritative running total from the service layer, so the
/// projection trusts it directly rather than computing deltas. This keeps the projection idempotent on
/// re-run because an event is only applied once (the projection driver gates on the checkpoint).
/// </summary>
public sealed class CurrencyBalanceProjection(IApplicationDbContext db) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "CurrencyCreditedEvent",
        "CurrencyDebitedEvent",
    };

    public string Name => "currency-balance";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        JObject? payload = TryParse(@event.PayloadJson);
        if (payload is null)
            return Result.Success();

        Guid? accountId =
            payload["AccountId"]?.Value<string>() is string s && Guid.TryParse(s, out Guid aid)
                ? aid
                : (Guid?)null;
        long? balanceAfter = payload["BalanceAfter"]?.Value<long?>();
        long? amount = payload["Amount"]?.Value<long?>();

        if (accountId is null || balanceAfter is null || amount is null)
            return Result.Success();

        CurrencyAccount? account = await db.CurrencyAccounts.FirstOrDefaultAsync(
            a => a.Id == accountId.Value && a.BroadcasterId == broadcasterId,
            cancellationToken
        );

        if (account is null)
            return Result.Success();

        account.Balance = balanceAfter.Value;
        account.LastActivityAt = @event.OccurredAt;

        switch (@event.EventType)
        {
            case "CurrencyCreditedEvent":
                account.LifetimeEarned += amount.Value;
                break;
            case "CurrencyDebitedEvent":
                account.LifetimeSpent += amount.Value;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Balance is NOT purely replay-safe — resetting it would leave accounts at 0 until replay completes.
        // We zero the columns here so replay can rebuild from the full event history.
        List<CurrencyAccount> accounts = await (
            broadcasterId is Guid id
                ? db.CurrencyAccounts.Where(a => a.BroadcasterId == id)
                : db.CurrencyAccounts
        ).ToListAsync(cancellationToken);

        foreach (CurrencyAccount account in accounts)
        {
            account.Balance = 0;
            account.LifetimeEarned = 0;
            account.LifetimeSpent = 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static JObject? TryParse(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
