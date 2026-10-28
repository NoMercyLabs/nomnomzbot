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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;
using ChannelConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Stores one channel's raid scoring rules as a JSON configuration row keyed by that channel.
/// Every read and write is scoped to the channel id; no channel can see another channel's rules.
/// </summary>
internal sealed class RaidScoringRulesStore(IApplicationDbContext db) : IRaidScoringRulesStore
{
    internal const string ConfigKey = "raids:scoring-rules";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<RaidScoringRules> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelConfiguration? row = await FindRowAsync(channelId, cancellationToken);
        if (row?.Value is null)
            return RaidScoringRules.Neutral;

        try
        {
            return RaidScoringDefaults.Resolve(
                JsonSerializer.Deserialize<RaidScoringRules>(row.Value, Json)
            );
        }
        catch (JsonException)
        {
            return RaidScoringRules.Neutral;
        }
    }

    public async Task<Result<RaidScoringRules>> SaveAsync(
        Guid channelId,
        RaidScoringRules rules,
        CancellationToken cancellationToken = default
    )
    {
        RaidScoringRules resolved = RaidScoringDefaults.Resolve(rules);
        Result validation = RaidScoringRulesValidator.Validate(resolved);
        if (validation.IsFailure)
            return Result.Failure<RaidScoringRules>(
                validation.ErrorMessage,
                validation.ErrorCode,
                validation.ErrorDetail,
                validation.ErrorData
            );

        string json = JsonSerializer.Serialize(resolved, Json);
        ChannelConfiguration? row = await FindRowAsync(channelId, cancellationToken);
        if (row is not null)
        {
            row.Value = json;
        }
        else
        {
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = channelId,
                    Key = ConfigKey,
                    Value = json,
                }
            );
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(resolved);
    }

    private Task<ChannelConfiguration?> FindRowAsync(
        Guid channelId,
        CancellationToken cancellationToken
    ) =>
        db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == ConfigKey,
            cancellationToken
        );
}
