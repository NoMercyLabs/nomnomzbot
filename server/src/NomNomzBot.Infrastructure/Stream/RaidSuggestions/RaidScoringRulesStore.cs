// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

        return JsonSerializer.Deserialize<RaidScoringRules>(row.Value, Json)
            ?? RaidScoringRules.Neutral;
    }

    public async Task<Result<RaidScoringRules>> SaveAsync(
        Guid channelId,
        RaidScoringRules rules,
        CancellationToken cancellationToken = default
    )
    {
        Result validation = RaidScoringRulesValidator.Validate(rules);
        if (validation.IsFailure)
            return Result.Failure<RaidScoringRules>(
                validation.ErrorMessage,
                validation.ErrorCode,
                validation.ErrorDetail
            );

        string json = JsonSerializer.Serialize(rules, Json);
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
        return Result.Success(rules);
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
