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
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Mini-games + fun-money gambling (economy.md §3.5). <see cref="PlayAsync"/> settles a play through the ledger:
/// the optional 18+ gate (only when <c>Requires18Plus</c>), the permission / bet-range / cooldown / per-stream
/// guards, the CSPRNG roll, and the bet/payout movements. (Deferred — documented: the bet debit + payout credit
/// are two ledger posts — each atomic on its own; the play row is written right after — consistent with the
/// catalog/jar services.)
/// </summary>
public sealed class GameService(
    IApplicationDbContext db,
    ICurrencyAccountService accounts,
    IAgeConsentService ageConsent,
    IGameRandomizer randomizer,
    IEventBus eventBus,
    TimeProvider clock
) : IGameService
{
    // The built-in game catalog — seeded lazily on first list call when the channel has no configs.
    // Each entry is (GameType, Category, WinChancePercent, HouseEdgePercent, PayoutMultiplier).
    private static readonly (
        string GameType,
        GameCategory Category,
        decimal? WinChance,
        decimal? HouseEdge,
        decimal? PayoutMultiplier
    )[] DefaultGames =
    [
        ("coinflip", GameCategory.Gambling, 50m, 5m, 1.9m),
        ("dice", GameCategory.Gambling, 50m, 5m, 1.9m),
        ("slots", GameCategory.Gambling, 30m, 20m, 2.5m),
        ("duel", GameCategory.Minigame, null, null, null),
    ];

    public async Task<Result<IReadOnlyList<GameConfigDto>>> ListGamesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<GameConfig> rows = await db
            .GameConfigs.Where(g => g.BroadcasterId == broadcasterId && g.DeletedAt == null)
            .OrderBy(g => g.GameType)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            rows = SeedDefaultGames(broadcasterId);
            db.GameConfigs.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        return Result.Success<IReadOnlyList<GameConfigDto>>([.. rows.Select(ToDto)]);
    }

    private static List<GameConfig> SeedDefaultGames(Guid broadcasterId) =>
        DefaultGames
            .Select(g => new GameConfig
            {
                BroadcasterId = broadcasterId,
                GameType = g.GameType,
                Category = g.Category,
                IsEnabled = false,
                Requires18Plus = false,
                WinChancePercent = g.WinChance,
                HouseEdgePercent = g.HouseEdge,
                PayoutMultiplier = g.PayoutMultiplier,
                CooldownSeconds = 0,
                Permission = "Everyone",
            })
            .ToList();

    public async Task<Result<GameConfigDto>> UpsertGameAsync(
        Guid broadcasterId,
        UpsertGameConfigRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.GameType))
            return Result.Failure<GameConfigDto>("Game type is required.", "VALIDATION_FAILED");
        if (!Enum.TryParse(request.Category, ignoreCase: true, out GameCategory category))
            return Result.Failure<GameConfigDto>(
                "Category must be minigame or gambling.",
                "VALIDATION_FAILED"
            );
        if (request.MinBet is long min && request.MaxBet is long max && min > max)
            return Result.Failure<GameConfigDto>(
                "MinBet cannot exceed MaxBet.",
                "VALIDATION_FAILED"
            );
        if (
            OutOfPercentRange(request.WinChancePercent)
            || OutOfPercentRange(request.HouseEdgePercent)
        )
            return Result.Failure<GameConfigDto>(
                "Win chance and house edge must be between 0 and 100.",
                "VALIDATION_FAILED"
            );
        if (request.PayoutMultiplier is < 0)
            return Result.Failure<GameConfigDto>(
                "Payout multiplier cannot be negative.",
                "VALIDATION_FAILED"
            );

        GameConfig? game = await db.GameConfigs.FirstOrDefaultAsync(
            g =>
                g.BroadcasterId == broadcasterId
                && g.GameType == request.GameType
                && g.DeletedAt == null,
            ct
        );
        bool isNew = game is null;
        game ??= new GameConfig { BroadcasterId = broadcasterId, GameType = request.GameType };
        if (isNew)
            db.GameConfigs.Add(game);

        game.Category = category;
        // Gambling is TOS-sensitive: a NEW gambling game is created disabled; the streamer opts in via an update.
        game.IsEnabled = isNew && category == GameCategory.Gambling ? false : request.IsEnabled;
        game.Requires18Plus = request.Requires18Plus;
        game.MinBet = request.MinBet;
        game.MaxBet = request.MaxBet;
        game.HouseEdgePercent = request.HouseEdgePercent;
        game.WinChancePercent = request.WinChancePercent;
        game.PayoutMultiplier = request.PayoutMultiplier;
        game.CooldownSeconds = request.CooldownSeconds;
        game.MaxPlaysPerStream = request.MaxPlaysPerStream;
        game.Permission = string.IsNullOrWhiteSpace(request.Permission)
            ? nameof(CommunityStanding.Everyone)
            : request.Permission;
        game.ConfigJson = request.Config is null
            ? null
            : JsonConvert.SerializeObject(request.Config);

        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(game));
    }

    public async Task<Result<GamePlayResultDto>> PlayAsync(
        Guid broadcasterId,
        PlayGameRequest request,
        CancellationToken ct = default
    )
    {
        GameConfig? game = await db.GameConfigs.FirstOrDefaultAsync(
            g =>
                g.BroadcasterId == broadcasterId
                && g.Id == request.GameConfigId
                && g.DeletedAt == null,
            ct
        );
        if (game is null)
            return Result.Failure<GamePlayResultDto>("Game not found.", "NOT_FOUND");
        if (!game.IsEnabled)
            return Result.Failure<GamePlayResultDto>("Game is disabled.", "GAMBLING_DISABLED");

        // The 18+ gate engages ONLY when the streamer turned it on (fun-money — not a compliance requirement).
        if (game.Requires18Plus)
        {
            Result<bool> granted = await ageConsent.HasGrantedAsync(
                broadcasterId,
                request.PlayerUserId,
                ct
            );
            if (granted.IsFailure)
                return Result.Failure<GamePlayResultDto>(granted.ErrorMessage, granted.ErrorCode);
            if (!granted.Value)
                return Result.Failure<GamePlayResultDto>(
                    "This game requires confirming you are 18 or older.",
                    "AGE_CONSENT_REQUIRED"
                );
        }

        int requiredLevel = Enum.TryParse(
            game.Permission,
            ignoreCase: true,
            out CommunityStanding standing
        )
            ? standing.ToLevel()
            : 0;
        if (request.RoleLevel < requiredLevel)
            return Result.Failure<GamePlayResultDto>(
                "Insufficient role to play this game.",
                "FORBIDDEN"
            );

        if (
            request.BetAmount <= 0
            || (game.MinBet is long minBet && request.BetAmount < minBet)
            || (game.MaxBet is long maxBet && request.BetAmount > maxBet)
        )
            return Result.Failure<GamePlayResultDto>(
                "Bet is outside the allowed range.",
                "BET_OUT_OF_RANGE"
            );

        if (game.CooldownSeconds > 0)
        {
            DateTime cutoff = clock.GetUtcNow().UtcDateTime.AddSeconds(-game.CooldownSeconds);
            bool onCooldown = await db.GamePlays.AnyAsync(
                p =>
                    p.BroadcasterId == broadcasterId
                    && p.GameConfigId == game.Id
                    && p.PlayerUserId == request.PlayerUserId
                    && p.CreatedAt > cutoff,
                ct
            );
            if (onCooldown)
                return Result.Failure<GamePlayResultDto>("Game is on cooldown.", "ON_COOLDOWN");
        }

        if (game.MaxPlaysPerStream is int maxPlays)
        {
            DateTime? streamStart = await EconomyStreamWindow.CurrentStreamStartAsync(
                db,
                broadcasterId,
                ct
            );
            if (streamStart is DateTime since)
            {
                int played = await db.GamePlays.CountAsync(
                    p =>
                        p.BroadcasterId == broadcasterId
                        && p.GameConfigId == game.Id
                        && p.PlayerUserId == request.PlayerUserId
                        && p.CreatedAt >= since,
                    ct
                );
                if (played >= maxPlays)
                    return Result.Failure<GamePlayResultDto>(
                        "Per-stream play limit reached for this game.",
                        "PER_STREAM_LIMIT"
                    );
            }
        }

        Result<CurrencyLedgerEntryDto> debit = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                request.PlayerUserId,
                -request.BetAmount,
                nameof(CurrencyEntryType.SpendGame),
                nameof(CurrencyLedgerSourceType.GameConfig),
                game.Id,
                EventId: null,
                Reason: null,
                ActorUserId: null,
                IdempotencyKey: null
            ),
            ct
        );
        if (debit.IsFailure)
            return Result.Failure<GamePlayResultDto>(debit.ErrorMessage, debit.ErrorCode);

        double winChance = (double)(game.WinChancePercent ?? 0m) / 100.0;
        bool won = randomizer.NextUnitInterval() < winChance;
        long payout = won
            ? (long)Math.Round(request.BetAmount * (double)(game.PayoutMultiplier ?? 0m))
            : 0;
        GameOutcome outcome = won ? GameOutcome.Win : GameOutcome.Lose;

        long balanceAfter = debit.Value.BalanceAfter;
        long? payoutEntryId = null;
        if (payout > 0)
        {
            Result<CurrencyLedgerEntryDto> credit = await accounts.PostLedgerEntryAsync(
                broadcasterId,
                new PostLedgerEntryCommand(
                    request.PlayerUserId,
                    payout,
                    nameof(CurrencyEntryType.EarnGame),
                    nameof(CurrencyLedgerSourceType.GameConfig),
                    game.Id,
                    EventId: null,
                    Reason: null,
                    ActorUserId: null,
                    IdempotencyKey: null
                ),
                ct
            );
            if (credit.IsSuccess)
            {
                payoutEntryId = credit.Value.Id;
                balanceAfter = credit.Value.BalanceAfter;
            }
        }

        long net = payout - request.BetAmount;
        GamePlay play = new()
        {
            BroadcasterId = broadcasterId,
            GameConfigId = game.Id,
            PlayerAccountId = debit.Value.AccountId,
            PlayerUserId = request.PlayerUserId,
            BetAmount = request.BetAmount,
            Outcome = outcome,
            PayoutAmount = payout,
            NetResult = net,
            BetLedgerEntryId = debit.Value.Id,
            PayoutLedgerEntryId = payoutEntryId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.GamePlays.Add(play);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new GamePlayedEvent
            {
                BroadcasterId = broadcasterId,
                GamePlayId = play.Id,
                GameConfigId = game.Id,
                GameType = game.GameType,
                PlayerUserId = request.PlayerUserId,
                BetAmount = request.BetAmount,
                Outcome = outcome.ToString(),
                PayoutAmount = payout,
                NetResult = net,
            },
            ct
        );
        return Result.Success(
            new GamePlayResultDto(
                play.Id,
                game.GameType,
                outcome.ToString(),
                request.BetAmount,
                payout,
                net,
                balanceAfter,
                Result: null
            )
        );
    }

    public async Task<Result<PagedList<GamePlayDto>>> GetGameHistoryAsync(
        Guid broadcasterId,
        GameHistoryFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<GamePlay> query = db.GamePlays.Where(p => p.BroadcasterId == broadcasterId);
        if (filter.GameConfigId is Guid configId)
            query = query.Where(p => p.GameConfigId == configId);
        if (filter.PlayerUserId is Guid player)
            query = query.Where(p => p.PlayerUserId == player);
        if (
            filter.Outcome is not null
            && Enum.TryParse(filter.Outcome, ignoreCase: true, out GameOutcome outcome)
        )
            query = query.Where(p => p.Outcome == outcome);

        int total = await query.CountAsync(ct);
        List<GamePlay> rows = await query
            .OrderByDescending(p => p.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<GamePlayDto>(
                [.. rows.Select(ToHistoryDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private static bool OutOfPercentRange(decimal? value) => value is < 0 or > 100;

    private static GameConfigDto ToDto(GameConfig g) =>
        new(
            g.Id,
            g.GameType,
            g.Category.ToString(),
            g.IsEnabled,
            g.Requires18Plus,
            g.MinBet,
            g.MaxBet,
            g.HouseEdgePercent,
            g.WinChancePercent,
            g.PayoutMultiplier,
            g.CooldownSeconds,
            g.MaxPlaysPerStream,
            g.Permission,
            g.ConfigJson is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, object?>>(g.ConfigJson)
        );

    private static GamePlayDto ToHistoryDto(GamePlay p) =>
        new(
            p.Id,
            p.GameConfigId,
            p.PlayerUserId,
            p.BetAmount,
            p.Outcome.ToString(),
            p.PayoutAmount,
            p.NetResult,
            p.CreatedAt
        );
}
