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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Currency definition + earning rules (economy.md §3.1). Pure CRUD with validation — no ledger effect.
/// </summary>
public sealed class CurrencyConfigService(IApplicationDbContext db, TimeProvider clock)
    : ICurrencyConfigService
{
    public async Task<Result<CurrencyConfigDto?>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        CurrencyConfig? config = await db.CurrencyConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        return Result.Success(config is null ? null : ToDto(config));
    }

    public async Task<Result<CurrencyConfigDto>> UpsertConfigAsync(
        Guid broadcasterId,
        UpsertCurrencyConfigRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.CurrencyName))
            return Result.Failure<CurrencyConfigDto>(
                "Currency name is required.",
                "VALIDATION_FAILED"
            );
        if (request.StartingBalance < 0)
            return Result.Failure<CurrencyConfigDto>(
                "Starting balance cannot be negative.",
                "VALIDATION_FAILED"
            );
        if (request.MaxBalance is long max && max < request.StartingBalance)
            return Result.Failure<CurrencyConfigDto>(
                "Max balance must be at least the starting balance.",
                "VALIDATION_FAILED"
            );

        CurrencyConfig? config = await db.CurrencyConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (config is null)
        {
            config = new CurrencyConfig { BroadcasterId = broadcasterId };
            db.CurrencyConfigs.Add(config);
        }
        config.CurrencyName = request.CurrencyName;
        config.CurrencyNamePlural = request.CurrencyNamePlural;
        config.IconUrl = request.IconUrl;
        config.IsEnabled = request.IsEnabled;
        config.StartingBalance = request.StartingBalance;
        config.MaxBalance = request.MaxBalance;
        config.DecimalPlaces = request.DecimalPlaces;
        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(config));
    }

    public async Task<Result<IReadOnlyList<EarningRuleDto>>> ListEarningRulesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<EarningRule> rules = await db
            .EarningRules.Where(r => r.BroadcasterId == broadcasterId && r.DeletedAt == null)
            .OrderBy(r => r.Source)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<EarningRuleDto>>([.. rules.Select(ToDto)]);
    }

    public async Task<Result<EarningRuleDto>> UpsertEarningRuleAsync(
        Guid broadcasterId,
        UpsertEarningRuleRequest request,
        CancellationToken ct = default
    )
    {
        if (!Enum.TryParse(request.Source, ignoreCase: true, out EarningSource source))
            return Result.Failure<EarningRuleDto>(
                $"Unknown earning source '{request.Source}'.",
                "VALIDATION_FAILED"
            );
        if (request.Rate < 0 || request.PerWindowCap < 0 || request.PerStreamCap < 0)
            return Result.Failure<EarningRuleDto>(
                "Rate and caps cannot be negative.",
                "VALIDATION_FAILED"
            );

        EarningRule? rule = await db.EarningRules.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.Source == source && r.DeletedAt == null,
            ct
        );
        if (rule is null)
        {
            rule = new EarningRule
            {
                BroadcasterId = broadcasterId,
                Source = source,
                ConfigSchemaVersion = 1,
            };
            db.EarningRules.Add(rule);
        }
        rule.IsEnabled = request.IsEnabled;
        rule.Rate = request.Rate;
        rule.UnitWindowSeconds = request.UnitWindowSeconds;
        rule.PerWindowCap = request.PerWindowCap;
        rule.PerStreamCap = request.PerStreamCap;
        rule.MinRoleLevel = request.MinRoleLevel;
        rule.BonusConfigJson = request.BonusConfig is null
            ? null
            : JsonConvert.SerializeObject(request.BonusConfig);
        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(rule));
    }

    public async Task<Result> DeleteEarningRuleAsync(
        Guid broadcasterId,
        Guid ruleId,
        CancellationToken ct = default
    )
    {
        EarningRule? rule = await db.EarningRules.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.Id == ruleId && r.DeletedAt == null,
            ct
        );
        if (rule is null)
            return Result.Failure("Earning rule not found.", "NOT_FOUND");

        rule.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static CurrencyConfigDto ToDto(CurrencyConfig c) =>
        new(
            c.Id,
            c.BroadcasterId,
            c.CurrencyName,
            c.CurrencyNamePlural,
            c.IconUrl,
            c.IsEnabled,
            c.StartingBalance,
            c.MaxBalance,
            c.DecimalPlaces,
            c.CreatedAt,
            c.UpdatedAt
        );

    private static EarningRuleDto ToDto(EarningRule r) =>
        new(
            r.Id,
            r.Source.ToString(),
            r.IsEnabled,
            r.Rate,
            r.UnitWindowSeconds,
            r.PerWindowCap,
            r.PerStreamCap,
            r.MinRoleLevel,
            r.ConfigSchemaVersion,
            r.BonusConfigJson is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, object?>>(r.BonusConfigJson)
        );
}
