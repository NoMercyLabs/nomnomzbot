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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>
/// Fulfills ONE drawn winner per the giveaway's prize mode (giveaways.md §4): <c>announce</c> posts one
/// bot line in chat naming the winner and the prize; <c>currency</c> credits a fixed amount or the summed entry-cost pot through the ledger;
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
    private readonly IReadOnlyDictionary<string, IPlatformDirectMessageSender> _dmSendersByProvider;
    private readonly IInboundOriginChatSender _chat;
    private readonly ITokenProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<GiveawayFulfillment> _logger;

    public GiveawayFulfillment(
        IApplicationDbContext db,
        ICurrencyAccountService accounts,
        IServiceProvider services,
        IEnumerable<IPlatformDirectMessageSender> dmSenders,
        IInboundOriginChatSender chat,
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
        _dmSendersByProvider = dmSenders.ToDictionary(s => s.Provider);
        _chat = chat;
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
            case GiveawayPrizeMode.Announce:
                await FulfillAnnouncementAsync(giveaway, winner, ct);
                break;
        }
    }

    // The announcement is bot speech: it goes out through the one bot sender, on the winner's platform.
    // A refused or faulted send must not unwind the draw; the winner row stands.
    private async Task FulfillAnnouncementAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        CancellationToken ct
    )
    {
        try
        {
            var user = await _db
                .Users.Where(u => u.Id == winner.ViewerUserId)
                .Select(u => new { u.DisplayName, u.Username })
                .FirstOrDefaultAsync(ct);
            string mention = user is null
                ? "winner"
                : ChatMentionName.For(user.DisplayName, user.Username);

            Result sent = await _chat.SendMessageAsync(
                giveaway.BroadcasterId,
                winner.Provider,
                $"Congratulations @{mention}, you won {giveaway.Title}!",
                ct
            );
            if (sent.IsFailure)
                _logger.LogWarning(
                    "Giveaway {GiveawayId} announcement for winner {WinnerId} was not sent: {Error}",
                    giveaway.Id,
                    winner.Id,
                    sent.ErrorMessage
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Giveaway {GiveawayId} announcement failed for winner {WinnerId}",
                giveaway.Id,
                winner.Id
            );
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
            // The pot = the summed PAID entry costs; it splits evenly across the winners DRAWN (not the
            // configured count), remainder to the earliest-drawn winner (integer currency — nothing vanishes).
            int paidEntries = await _db.GiveawayEntries.CountAsync(
                e => e.GiveawayId == giveaway.Id && e.EntryCostLedgerEntryId != null,
                ct
            );
            int drawnWinners = await _db.GiveawayWinners.CountAsync(
                w => w.GiveawayId == giveaway.Id && w.Status != GiveawayWinnerStatus.Redrawn,
                ct
            );
            long pot = paidEntries * (giveaway.EntryCost ?? 0);
            long share = pot / Math.Max(drawnWinners, 1);
            // Winners paid earlier in this same draw are not saved yet, so the tracked rows count too.
            bool isFirstWinner =
                !_db.GiveawayWinners.Local.Any(w =>
                    w.GiveawayId == giveaway.Id && w.FulfillmentLedgerEntryId != null
                )
                && !await _db.GiveawayWinners.AnyAsync(
                    w => w.GiveawayId == giveaway.Id && w.FulfillmentLedgerEntryId != null,
                    ct
                );
            amount = isFirstWinner ? share + pot % Math.Max(drawnWinners, 1) : share;
        }
        else
        {
            amount = giveaway.PrizeCurrencyAmount ?? 0;
        }

        if (amount <= 0)
            return;

        Result<CurrencyLedgerEntryDto> credit = await _accounts.PostLedgerEntryAsync(
            giveaway.BroadcasterId,
            new(
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
        BindingTarget? target = giveaway.GetBindingTarget();
        if (target is null)
            return;

        string displayName =
            await _db
                .Users.Where(u => u.Id == winner.ViewerUserId)
                .Select(u => u.DisplayName ?? u.Username ?? "winner")
                .FirstOrDefaultAsync(ct)
            ?? "winner";

        if (target.Kind is BindingTargetKind.Command or BindingTargetKind.Action)
        {
            await RunDirectTargetAsync(giveaway, winner, target, displayName, ct);
            return;
        }

        if (giveaway.PrizePipelineId is not { } pipelineId)
            return;

        try
        {
            IPipelineEngine pipelines = _services.GetRequiredService<IPipelineEngine>();
            await pipelines.ExecuteAsync(
                new()
                {
                    BroadcasterId = giveaway.BroadcasterId,
                    PipelineId = pipelineId,
                    TriggeredByUserId = winner.ViewerUserId.ToString(),
                    TriggeredByDisplayName = displayName,
                },
                ct
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
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

    // A prize bound straight to a command or an action runs through the one invoker, with the winner as
    // the invoker: no wrapper pipeline. A fault must not unwind the draw, same as the pipeline path.
    private async Task RunDirectTargetAsync(
        Giveaway giveaway,
        GiveawayWinner winner,
        BindingTarget target,
        string displayName,
        CancellationToken ct
    )
    {
        try
        {
            ICommandInvoker invoker = _services.GetRequiredService<ICommandInvoker>();
            Result<InvocationOutcome> run = await invoker.InvokeAsync(
                giveaway.BroadcasterId,
                target,
                new(
                    winner.ProviderUserId ?? winner.ViewerTwitchUserId,
                    displayName,
                    string.Empty,
                    "giveaway"
                ),
                ct
            );
            if (run.IsFailure || !run.Value!.Succeeded)
                _logger.LogWarning(
                    "Giveaway {GiveawayId} prize could not run its {Kind} target for winner {WinnerId}: {Error}",
                    giveaway.Id,
                    target.Kind,
                    winner.Id,
                    run.IsFailure ? run.ErrorMessage : run.Value!.Error
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Giveaway {GiveawayId} prize {Kind} target failed for winner {WinnerId}",
                giveaway.Id,
                target.Kind,
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

        if (
            !_dmSendersByProvider.TryGetValue(
                winner.Provider,
                out IPlatformDirectMessageSender? sender
            )
        )
        {
            // D6: no sender for this platform is exactly the "couldn't deliver" case — the code stays
            // assigned and the broadcaster reveals it manually; never lost, never silently no-op'd.
            winner.WhisperDelivered = false;
            _logger.LogWarning(
                "Giveaway code delivery skipped for winner {WinnerId} — no DM sender registered for provider {Provider}; code stays assigned for broadcaster reveal",
                winner.Id,
                winner.Provider
            );
            return;
        }

        Result delivered = await sender.SendAsync(
            giveaway.BroadcasterId,
            winner.ProviderUserId ?? winner.ViewerTwitchUserId,
            $"You won \"{giveaway.Title}\"! Your code: {plaintext}",
            ct
        );
        if (delivered.IsSuccess)
        {
            code.Status = GiveawayCodeStatus.Delivered;
            winner.WhisperDelivered = true;
        }
        else
        {
            // D6: the code stays assigned and the broadcaster reveals it manually — never lost.
            winner.WhisperDelivered = false;
            _logger.LogWarning(
                "Giveaway code delivery failed for winner {WinnerId} ({Error}) — code stays assigned for broadcaster reveal",
                winner.Id,
                delivered.ErrorMessage
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
