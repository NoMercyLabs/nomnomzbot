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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

public sealed class TtsBudgetWarningSource(IApplicationDbContext db, TimeProvider clock)
    : IActionRequiredSource
{
    private const string Prefix = "tts-budget:";

    public string SourceKey => "tts_budgets";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [Prefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(TtsBudgetWarningEvent),
        nameof(TtsUtteranceDispatchedEvent),
        nameof(ChannelConfigChangedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        TtsConfig? config = await db.TtsConfigs.FirstOrDefaultAsync(
            row => row.BroadcasterId == channelId,
            cancellationToken
        );
        if (config is null)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<ActionRequiredItemDto> items = [];
        foreach (TtsProviderBudgetDto budget in TtsBudgetPlan.Parse(config.ProviderBudgetsJson))
        {
            TtsBudgetUsage usage = await TtsBudgetLedger.ReadAsync(
                db,
                channelId,
                budget.Provider,
                "default",
                now,
                cancellationToken
            );
            Add(
                items,
                dismissedKeys,
                now,
                budget.Provider,
                null,
                budget.MonthlyCharacters,
                usage.ProviderUsed
            );
            if (budget.Shares.Count == 0)
                continue;
            foreach (TtsBudgetShareDto share in TtsBudgetPlan.EffectiveShares(budget))
            {
                TtsBudgetUsage shareUsage = await TtsBudgetLedger.ReadAsync(
                    db,
                    channelId,
                    budget.Provider,
                    share.Name,
                    now,
                    cancellationToken
                );
                Add(
                    items,
                    dismissedKeys,
                    now,
                    budget.Provider,
                    share.Name,
                    share.MonthlyCharacters,
                    shareUsage.ShareUsed
                );
            }
        }
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static void Add(
        List<ActionRequiredItemDto> items,
        IReadOnlySet<string> dismissedKeys,
        DateTime now,
        string provider,
        string? share,
        int limit,
        int used
    )
    {
        if (limit <= 0 || (long)used * 100 < (long)limit * 80)
            return;
        int threshold = used >= limit ? 100 : 80;
        string key = $"{Prefix}{now:yyyyMM}:{provider}:{share ?? "all"}:{threshold}";
        if (dismissedKeys.Contains(key))
            return;
        items.Add(
            new(
                key,
                "tts_budget_warning",
                threshold == 100 ? "critical" : "warning",
                "attention_tts_budget_title",
                "attention_tts_budget_message",
                new()
                {
                    ["provider"] = provider,
                    ["share"] = share ?? "all",
                    ["threshold"] = threshold.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    ["used"] = used.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                now,
                "tts?view=keys",
                null,
                null,
                1,
                []
            )
        );
    }
}
