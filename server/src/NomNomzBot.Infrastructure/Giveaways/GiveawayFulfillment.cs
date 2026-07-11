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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>
/// Fulfills ONE drawn winner per the giveaway's prize mode (giveaways.md §4): <c>announce</c> records
/// only; <c>currency</c> credits a fixed amount or the summed entry-cost pot through the ledger;
/// <c>pipeline</c> runs the prize pipeline with the winner as the triggering user; <c>code_pool</c>
/// atomically claims an available code and whispers the decrypted plaintext — a failed whisper leaves
/// the code <c>assigned</c> with <c>WhisperDelivered=false</c> for the broadcaster reveal (D6). Extracted
/// from the draw so the redraw re-runs fulfillment identically.
/// </summary>
public interface IGiveawayFulfillment
{
    Task FulfillAsync(Giveaway giveaway, GiveawayWinner winner, CancellationToken ct = default);
}

public sealed class GiveawayFulfillment : IGiveawayFulfillment
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrencyAccountService _accounts;
    private readonly IServiceProvider _services;
    private readonly ITwitchWhispersApi _whispers;
    private readonly ITokenProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<GiveawayFulfillment> _logger;

    public GiveawayFulfillment(
        IApplicationDbContext db,
        ICurrencyAccountService accounts,
        IServiceProvider services,
        ITwitchWhispersApi whispers,
        ITokenProtector protector,
        TimeProvider clock,
        ILogger<GiveawayFulfillment> logger
    )
    {
        _db = db;
        _accounts = accounts;
        // The pipeline engine is resolved LAZILY at fulfillment time: the engine materializes every
        // ICommandAction eagerly, and the giveaway actions depend on IGiveawayService → this fulfillment
        // — a constructor-injected engine here closes that loop into a DI cycle.
        _services = services;
        _whispers = whispers;
        _protector = protector;
        _clock = clock;
        _logger = logger;
    }

    public async Task FulfillAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        CancellationToken ct = default
    )
    {
        switch (giveaway.PrizeMode)
        {
            case GiveawayPrizeMode.Currency:
                await FulfillCurrencyAsync(giveaway, winner, ct);
                break;
            case GiveawayPrizeMode.Pipeline:
                await FulfillPipelineAsync(giveaway, winner, ct);
                break;
            case GiveawayPrizeMode.CodePool:
                await FulfillCodeAsync(giveaway, winner, ct);
                break;
            // announce: recording the winner IS the fulfillment; GiveawayDrawnEvent carries the rest.
        }
    }

    private async Task FulfillCurrencyAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        CancellationToken ct
    )
    {
        long amount;
        if (giveaway.PrizeFromPot)
        {
            // The pot = the summed PAID entry costs; multi-winner splits evenly, remainder to the
            // earliest-drawn winner (integer currency — nothing vanishes).
            int paidEntries = await _db.GiveawayEntries.CountAsync(
                e => e.GiveawayId == giveaway.Id && e.EntryCostLedgerEntryId != null,
                ct
            );
            long pot = paidEntries * (giveaway.EntryCost ?? 0);
            long share = pot / Math.Max(giveaway.WinnerCount, 1);
            bool isFirstWinner = !await _db.GiveawayWinners.AnyAsync(
                w => w.GiveawayId == giveaway.Id && w.FulfillmentLedgerEntryId != null,
                ct
            );
            amount = isFirstWinner ? share + pot % Math.Max(giveaway.WinnerCount, 1) : share;
        }
        else
        {
            amount = giveaway.PrizeCurrencyAmount ?? 0;
        }

        if (amount <= 0)
            return;

        Result<CurrencyLedgerEntryDto> credit = await _accounts.PostLedgerEntryAsync(
            giveaway.BroadcasterId,
            new PostLedgerEntryCommand(
                winner.ViewerUserId,
                amount,
                nameof(CurrencyEntryType.EarnGiveaway),
                nameof(CurrencyLedgerSourceType.Giveaway),
                SourceId: giveaway.Id,
                EventId: null,
                Reason: $"Giveaway prize: {giveaway.Title}",
                ActorUserId: null,
                IdempotencyKey: $"giveaway-prize:{giveaway.Id}:{winner.Id}"
            ),
            ct
        );
        if (credit.IsSuccess)
            winner.FulfillmentLedgerEntryId = credit.Value.Id;
        else
            _logger.LogWarning(
                "Giveaway currency payout failed for winner {WinnerId}: {Error}",
                winner.Id,
                credit.ErrorMessage
            );
    }

    private async Task FulfillPipelineAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        CancellationToken ct
    )
    {
        if (giveaway.PrizePipelineId is not { } pipelineId)
            return;

        string displayName =
            await _db
                .Users.Where(u => u.Id == winner.ViewerUserId)
                .Select(u => u.DisplayName ?? u.Username ?? "winner")
                .FirstOrDefaultAsync(ct)
            ?? "winner";

        try
        {
            IPipelineEngine pipelines = _services.GetRequiredService<IPipelineEngine>();
            await pipelines.ExecuteAsync(
                new PipelineRequest
                {
                    BroadcasterId = giveaway.BroadcasterId,
                    PipelineId = pipelineId,
                    TriggeredByUserId = winner.ViewerUserId.ToString(),
                    TriggeredByDisplayName = displayName,
                },
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A prize-pipeline fault must not unwind the draw — the winner row stands; the broadcaster
            // re-runs via redraw or manually.
            _logger.LogError(
                ex,
                "Giveaway prize pipeline {PipelineId} failed for winner {WinnerId}",
                pipelineId,
                winner.Id
            );
        }
    }

    private async Task FulfillCodeAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        CancellationToken ct
    )
    {
        if (giveaway.PrizeCodePoolId is not { } poolId)
            return;

        // Atomic claim: the oldest available code flips to assigned in the surrounding draw transaction.
        GiveawayCode? code = await _db
            .GiveawayCodes.Where(c =>
                c.CodePoolId == poolId && c.Status == GiveawayCodeStatus.Available
            )
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (code is null)
        {
            // Exhaustion is flagged on the winner (AssignedCodeId stays null) — the draw surfaces
            // CODE_POOL_EXHAUSTED; never silently dropped.
            _logger.LogWarning(
                "Giveaway code pool {PoolId} exhausted — winner {WinnerId} has no code",
                poolId,
                winner.Id
            );
            return;
        }

        code.Status = GiveawayCodeStatus.Assigned;
        code.AssignedWinnerId = winner.Id;
        code.AssignedAt = _clock.GetUtcNow().UtcDateTime;
        winner.AssignedCodeId = code.Id;

        string? plaintext = await _protector.TryUnprotectAsync(
            code.CodeCipher,
            GiveawayCodeProtection.Context(giveaway.BroadcasterId),
            ct
        );
        if (plaintext is null)
        {
            winner.WhisperDelivered = false;
            _logger.LogError(
                "Giveaway code {CodeId} could not be decrypted — flagged for broadcaster attention",
                code.Id
            );
            return;
        }

        Result whispered = await _whispers.SendWhisperAsync(
            giveaway.BroadcasterId,
            winner.ViewerTwitchUserId,
            $"You won \"{giveaway.Title}\"! Your code: {plaintext}",
            ct
        );
        if (whispered.IsSuccess)
        {
            code.Status = GiveawayCodeStatus.Delivered;
            winner.WhisperDelivered = true;
        }
        else
        {
            // D6: the code stays assigned and the broadcaster reveals it manually — never lost.
            winner.WhisperDelivered = false;
            _logger.LogWarning(
                "Giveaway code whisper failed for winner {WinnerId} ({Error}) — code stays assigned for broadcaster reveal",
                winner.Id,
                whispered.ErrorMessage
            );
        }
    }
}

/// <summary>The ONE protection context for giveaway prize codes — write and read must agree byte-for-byte.</summary>
public static class GiveawayCodeProtection
{
    public static TokenProtectionContext Context(Guid broadcasterId) =>
        new(broadcasterId.ToString(), "giveaway_code", "code");
}
