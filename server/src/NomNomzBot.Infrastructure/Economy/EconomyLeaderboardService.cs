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

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Economy leaderboards (economy.md §3.8), excluding opted-out viewers; closed periods freeze into append-only
/// snapshots. An <c>alltime</c> ranking (and any <c>balance</c> metric) reads the <see cref="CurrencyAccount"/>
/// projection; a bounded period (daily / weekly / monthly) folds the ledger over the window for the earned /
/// spent metrics. (Deferred — documented: jar-scope rankings; the display name falls back to the account's
/// Twitch id pending a Users-join enrichment; and the opt-out's ConsentRecords marker awaits the IConsentService
/// owner of the now-built ConsentRecords ledger.)
/// </summary>
public sealed class EconomyLeaderboardService(IApplicationDbContext db, TimeProvider clock)
    : IEconomyLeaderboardService
{
    private static readonly HashSet<string> AllowedMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "balance",
        "earned",
        "spent",
    };
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "channel",
        "jar",
    };

    public async Task<Result<IReadOnlyList<LeaderboardConfigDto>>> ListConfigsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<LeaderboardConfig> rows = await db
            .LeaderboardConfigs.Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .OrderBy(c => c.Metric)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<LeaderboardConfigDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<Result<LeaderboardConfigDto>> UpsertConfigAsync(
        Guid broadcasterId,
        UpsertLeaderboardConfigRequest request,
        CancellationToken ct = default
    )
    {
        string metric = (request.Metric ?? string.Empty).Trim().ToLowerInvariant();
        string scope = (request.Scope ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedMetrics.Contains(metric))
            return Result.Failure<LeaderboardConfigDto>(
                "Metric must be balance, earned, or spent.",
                "VALIDATION_FAILED"
            );
        if (!AllowedScopes.Contains(scope))
            return Result.Failure<LeaderboardConfigDto>(
                "Scope must be channel or jar.",
                "VALIDATION_FAILED"
            );
        if (request.TopN <= 0)
            return Result.Failure<LeaderboardConfigDto>(
                "TopN must be positive.",
                "VALIDATION_FAILED"
            );

        LeaderboardConfig config;
        if (request.Id is Guid id)
        {
            LeaderboardConfig? existing = await db.LeaderboardConfigs.FirstOrDefaultAsync(
                c => c.Id == id && c.BroadcasterId == broadcasterId && c.DeletedAt == null,
                ct
            );
            if (existing is null)
                return Result.Failure<LeaderboardConfigDto>("Config not found.", "NOT_FOUND");
            config = existing;
        }
        else
        {
            config = new LeaderboardConfig { BroadcasterId = broadcasterId };
            db.LeaderboardConfigs.Add(config);
        }

        config.Metric = metric;
        config.Scope = scope;
        config.Period = (request.Period ?? "alltime").Trim().ToLowerInvariant();
        config.IsPublic = request.IsPublic;
        config.TopN = request.TopN;
        config.JarId = request.JarId;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(config));
    }

    public async Task<Result> DeleteConfigAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    )
    {
        LeaderboardConfig? config = await db.LeaderboardConfigs.FirstOrDefaultAsync(
            c => c.Id == configId && c.BroadcasterId == broadcasterId && c.DeletedAt == null,
            ct
        );
        if (config is null)
            return Result.Failure("Config not found.", "NOT_FOUND");
        config.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<LeaderboardEntryDto>>> GetRankingAsync(
        Guid broadcasterId,
        Guid configId,
        int? top,
        CancellationToken ct = default
    )
    {
        LeaderboardConfig? config = await db.LeaderboardConfigs.FirstOrDefaultAsync(
            c => c.Id == configId && c.BroadcasterId == broadcasterId && c.DeletedAt == null,
            ct
        );
        if (config is null)
            return Result.Failure<IReadOnlyList<LeaderboardEntryDto>>(
                "Config not found.",
                "NOT_FOUND"
            );

        List<(CurrencyAccount Account, long Value)> ranked = await RankedAccountsAsync(
            broadcasterId,
            config,
            top ?? config.TopN,
            ct
        );
        return Result.Success<IReadOnlyList<LeaderboardEntryDto>>([
            .. ranked.Select(
                (r, i) =>
                    new LeaderboardEntryDto(
                        i + 1,
                        r.Account.ViewerUserId,
                        r.Account.Id,
                        r.Account.ViewerTwitchUserId,
                        r.Value
                    )
            ),
        ]);
    }

    public async Task<Result<IReadOnlyList<LeaderboardEntryDto>>> CaptureSnapshotAsync(
        Guid broadcasterId,
        Guid configId,
        string periodKey,
        CancellationToken ct = default
    )
    {
        LeaderboardConfig? config = await db.LeaderboardConfigs.FirstOrDefaultAsync(
            c => c.Id == configId && c.BroadcasterId == broadcasterId && c.DeletedAt == null,
            ct
        );
        if (config is null)
            return Result.Failure<IReadOnlyList<LeaderboardEntryDto>>(
                "Config not found.",
                "NOT_FOUND"
            );

        List<(CurrencyAccount Account, long Value)> ranked = await RankedAccountsAsync(
            broadcasterId,
            config,
            config.TopN,
            ct
        );
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<LeaderboardEntryDto> entries = [];
        int rank = 1;
        foreach ((CurrencyAccount account, long value) in ranked)
        {
            db.LeaderboardSnapshots.Add(
                new LeaderboardSnapshot
                {
                    LeaderboardConfigId = config.Id,
                    BroadcasterId = broadcasterId,
                    PeriodKey = periodKey,
                    Rank = rank,
                    SubjectAccountId = account.Id,
                    SubjectUserId = account.ViewerUserId,
                    SubjectTwitchUserId = account.ViewerTwitchUserId,
                    DisplayNameSnapshot = account.ViewerTwitchUserId,
                    Value = value,
                    CapturedAt = now,
                }
            );
            entries.Add(
                new LeaderboardEntryDto(
                    rank,
                    account.ViewerUserId,
                    account.Id,
                    account.ViewerTwitchUserId,
                    value
                )
            );
            rank++;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success<IReadOnlyList<LeaderboardEntryDto>>(entries);
    }

    public async Task<Result> OptOutAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        LeaderboardOptOut? existing = await db
            .LeaderboardOptOuts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                o => o.BroadcasterId == broadcasterId && o.ViewerUserId == viewerUserId,
                ct
            );
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (existing is null)
            db.LeaderboardOptOuts.Add(
                new LeaderboardOptOut
                {
                    BroadcasterId = broadcasterId,
                    ViewerUserId = viewerUserId,
                    ViewerTwitchUserId = string.Empty,
                    OptedOutAt = now,
                }
            );
        else if (existing.DeletedAt is not null)
        {
            existing.DeletedAt = null; // re-activate a previously opted-in row
            existing.OptedOutAt = now;
        }
        // else already opted out — idempotent no-op
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> OptInAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        LeaderboardOptOut? existing = await db.LeaderboardOptOuts.FirstOrDefaultAsync(
            o =>
                o.BroadcasterId == broadcasterId
                && o.ViewerUserId == viewerUserId
                && o.DeletedAt == null,
            ct
        );
        if (existing is not null)
        {
            existing.DeletedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    private async Task<List<(CurrencyAccount Account, long Value)>> RankedAccountsAsync(
        Guid broadcasterId,
        LeaderboardConfig config,
        int take,
        CancellationToken ct
    )
    {
        if (!string.Equals(config.Scope, "channel", StringComparison.OrdinalIgnoreCase))
            return []; // jar-scope ranking deferred

        List<Guid> optedOut = await db
            .LeaderboardOptOuts.Where(o => o.BroadcasterId == broadcasterId && o.DeletedAt == null)
            .Select(o => o.ViewerUserId)
            .ToListAsync(ct);

        string metric = config.Metric.ToLowerInvariant();
        // Balance is point-in-time (no window); earned/spent over a BOUNDED period fold the ledger.
        DateTime? windowStart = metric == "balance" ? null : PeriodStart(config.Period);
        if (windowStart is DateTime since)
            return await RankByLedgerWindowAsync(broadcasterId, metric, since, optedOut, take, ct);

        IQueryable<CurrencyAccount> query = db.CurrencyAccounts.Where(a =>
            a.BroadcasterId == broadcasterId
            && a.DeletedAt == null
            && !optedOut.Contains(a.ViewerUserId)
        );
        query = metric switch
        {
            "earned" => query.OrderByDescending(a => a.LifetimeEarned),
            "spent" => query.OrderByDescending(a => a.LifetimeSpent),
            _ => query.OrderByDescending(a => a.Balance),
        };
        List<CurrencyAccount> rows = await query.Take(take).ToListAsync(ct);
        return [.. rows.Select(a => (a, MetricValue(metric, a)))];
    }

    /// <summary>Ranks earned/spent over the period window by folding the ledger (positive inflow / absolute outflow).</summary>
    private async Task<List<(CurrencyAccount Account, long Value)>> RankByLedgerWindowAsync(
        Guid broadcasterId,
        string metric,
        DateTime since,
        List<Guid> optedOut,
        int take,
        CancellationToken ct
    )
    {
        IQueryable<CurrencyLedgerEntry> entries = db.CurrencyLedgerEntries.Where(e =>
            e.BroadcasterId == broadcasterId
            && e.CreatedAt >= since
            && !optedOut.Contains(e.ViewerUserId)
        );
        entries =
            metric == "spent" ? entries.Where(e => e.Amount < 0) : entries.Where(e => e.Amount > 0);

        // Aggregate the per-viewer sums DB-side, then order + take in memory (the InMemory test provider
        // cannot translate an OrderBy over a GroupBy projection; the group set is bounded by viewer count).
        List<ViewerTotal> totals = await entries
            .GroupBy(e => e.ViewerUserId)
            .Select(g => new ViewerTotal(g.Key, g.Sum(e => e.Amount)))
            .ToListAsync(ct);
        List<ViewerTotal> top =
        [
            .. (
                metric == "spent"
                    ? totals.OrderBy(t => t.Total) // most negative = most spent
                    : totals.OrderByDescending(t => t.Total)
            ).Take(take),
        ];

        List<Guid> ids = [.. top.Select(t => t.ViewerUserId)];
        Dictionary<Guid, CurrencyAccount> accounts = await db
            .CurrencyAccounts.Where(a =>
                a.BroadcasterId == broadcasterId && ids.Contains(a.ViewerUserId)
            )
            .ToDictionaryAsync(a => a.ViewerUserId, ct);

        List<(CurrencyAccount, long)> result = [];
        foreach (ViewerTotal t in top)
            if (accounts.TryGetValue(t.ViewerUserId, out CurrencyAccount? account))
                result.Add((account, metric == "spent" ? -t.Total : t.Total));
        return result;
    }

    private DateTime? PeriodStart(string period)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return period.ToLowerInvariant() switch
        {
            "daily" => now.Date,
            "weekly" => now.Date.AddDays(-(int)now.DayOfWeek),
            "monthly" => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null, // alltime / unknown → the lifetime projection
        };
    }

    private static long MetricValue(string metric, CurrencyAccount a) =>
        metric switch
        {
            "earned" => a.LifetimeEarned,
            "spent" => a.LifetimeSpent,
            _ => a.Balance,
        };

    private readonly record struct ViewerTotal(Guid ViewerUserId, long Total);

    private static LeaderboardConfigDto ToDto(LeaderboardConfig c) =>
        new(
            c.Id,
            c.BroadcasterId,
            c.JarId,
            c.Metric,
            c.Scope,
            c.Period,
            c.IsPublic,
            c.TopN,
            c.CreatedAt,
            c.UpdatedAt
        );
}
