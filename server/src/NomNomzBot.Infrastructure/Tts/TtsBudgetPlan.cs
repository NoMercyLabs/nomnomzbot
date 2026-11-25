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

using System.Text.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

public static class TtsBudgetPlan
{
    public static Result Validate(
        IReadOnlyList<TtsProviderBudgetDto>? budgets,
        IReadOnlyList<string>? fallback
    )
    {
        if (budgets is not null)
        {
            HashSet<string> providers = new(StringComparer.Ordinal);
            foreach (TtsProviderBudgetDto budget in budgets)
            {
                if (
                    !TtsConfigOptions.Providers.Contains(budget.Provider)
                    || !providers.Add(budget.Provider)
                    || budget.MonthlyCharacters < 1
                )
                    return Errors.ValidationFailed(
                        "Every TTS budget needs a unique known provider and a positive limit."
                    );
                HashSet<string> shares = new(StringComparer.Ordinal);
                long allocated = 0;
                foreach (TtsBudgetShareDto share in budget.Shares)
                {
                    if (
                        string.IsNullOrWhiteSpace(share.Name)
                        || share.Name != share.Name.Trim()
                        || share.Name == "default"
                        || share.Name.Length > 80
                        || !shares.Add(share.Name)
                        || share.MonthlyCharacters < 1
                    )
                        return Errors.ValidationFailed(
                            "TTS shares need unique names and positive limits."
                        );
                    allocated += share.MonthlyCharacters;
                }
                if (allocated > budget.MonthlyCharacters)
                    return Errors.ValidationFailed("TTS share limits exceed the provider budget.");
            }
        }
        if (fallback is not null)
        {
            HashSet<string> providers = new(StringComparer.Ordinal);
            foreach (string provider in fallback)
            {
                if (!TtsConfigOptions.Providers.Contains(provider) || !providers.Add(provider))
                    return Errors.ValidationFailed(
                        "The TTS fallback order has an unknown or repeated provider."
                    );
                if (
                    budgets is { Count: > 0 }
                    && !budgets.Any(budget => budget.Provider == provider)
                )
                    return Errors.ValidationFailed(
                        $"TTS fallback provider '{provider}' needs a monthly budget."
                    );
            }
        }
        return Result.Success();
    }

    // A config saved before budgets existed stores an empty string (the migration default): no budgets.
    public static IReadOnlyList<TtsProviderBudgetDto> Parse(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<TtsProviderBudgetDto>>(json) ?? [];

    public static IReadOnlyList<string> ParseFallback(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    public static IReadOnlyList<TtsBudgetShareDto> EffectiveShares(TtsProviderBudgetDto budget)
    {
        int remaining =
            budget.MonthlyCharacters - budget.Shares.Sum(share => share.MonthlyCharacters);
        List<TtsBudgetShareDto> shares = [.. budget.Shares];
        shares.Insert(0, new("default", remaining));
        return shares;
    }
}
