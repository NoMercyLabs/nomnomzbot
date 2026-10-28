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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>
/// Condition: pass with a given probability.
/// Usage: { "type": "random", "chance": 0.5 }  (50% chance)
///        { "type": "random", "percent": 25 }   (25% chance)
/// </summary>
public sealed class RandomCondition : ICommandCondition
{
    public string ConditionType => "random";

    public Task<bool> EvaluateAsync(PipelineExecutionContext ctx, ConditionDefinition condition)
    {
        double threshold = 0.5;

        if (condition.Parameters is not null)
        {
            if (
                condition.Parameters.TryGetValue("chance", out JsonElement chance)
                && chance.ValueKind == JsonValueKind.Number
            )
            {
                threshold = chance.GetDouble();
            }
            else if (
                condition.Parameters.TryGetValue("percent", out JsonElement pct)
                && pct.ValueKind == JsonValueKind.Number
            )
            {
                threshold = pct.GetDouble() / 100.0;
            }
        }

        return Task.FromResult(Random.Shared.NextDouble() < threshold);
    }
}
