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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// The generic orchestrator (live-games.md §3.1): one engine runs every discovered game. It owns ALL side
/// effects — persistence (state snapshot per transition, D9), currency via the three <see cref="IGameService"/>
/// live-game methods (D4), overlay frames via the existing widget push (D5), and the domain events — so a
/// game stays pure logic. The scoped engine shares live state with the runner and the chat listener through
/// the singleton <see cref="LiveGameSessionRegistry"/>; per-session mutation serializes on the runtime gate.
/// </summary>
public sealed class LiveGameEngine(
    IApplicationDbContext db,
    IGameService games,
    IWidgetEventNotifier overlay,
    ILiveGameCatalog catalog,
    ILiveGameOverlayResolver overlayResolver,
    LiveGameSessionRegistry registry,
    IGameRandomizer randomizer,
    IEventBus eventBus,
    TimeProvider clock,
    ILogger<LiveGameEngine> logger
) : ILiveGameEngine
{
    private static readonly GameSessionStatus[] NonTerminal =
    [
        GameSessionStatus.Lobby,
        GameSessionStatus.Running,
        GameSessionStatus.Resolving,
    ];

    public async Task<Result<GameSessionDto>> StartAsync(
        Guid broadcasterId,
        StartLiveGameCommand command,
        CancellationToken ct = default
    )
    {
        if (!catalog.TryGet(command.GameType, out ILiveGame? game))
            return Result.Failure<GameSessionDto>(
                $"Unknown live game '{command.GameType}'.",
                "UNKNOWN_GAME"
            );

        GameConfig? config = await db.GameConfigs.FirstOrDefaultAsync(
            g =>
                g.BroadcasterId == broadcasterId
                && g.GameType == game.GameKey
                && g.DeletedAt == null,
            ct
        );
        if (config is null)
            return Result.Failure<GameSessionDto>(
                $"No game config exists for '{game.GameKey}' on this channel.",
                "GAME_NOT_CONFIGURED"
            );
        if (!config.IsEnabled)
            return Result.Failure<GameSessionDto>("Game is disabled.", "GAME_DISABLED");

        // D7 — the overlay shows one game at a time. Both the in-memory registry (this node) and the DB
        // (any node / pre-sweep leftovers) must be clear.
        if (registry.TryGet(broadcasterId, out _))
            return Result.Failure<GameSessionDto>(
                "A live game session is already active.",
                "SESSION_ALREADY_ACTIVE"
            );
        bool hasNonTerminal = await db.GameSessions.AnyAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.DeletedAt == null
                && NonTerminal.Contains(s.Status),
            ct
        );
        if (hasNonTerminal)
            return Result.Failure<GameSessionDto>(
                "A live game session is already active.",
                "SESSION_ALREADY_ACTIVE"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        LiveGameManifest manifest = game.Manifest;
        GameSession session = new()
        {
            BroadcasterId = broadcasterId,
            GameConfigId = config.Id,
            GameType = game.GameKey,
            Status = GameSessionStatus.Lobby,
            StartedByUserId = command.StartedByUserId,
            StartedAt = now,
            JoinClosesAt = now + manifest.LobbyWindow,
        };
        db.GameSessions.Add(session);
        await db.SaveChangesAsync(ct);

        LiveGameSessionRuntime runtime = new()
        {
            SessionId = session.Id,
            BroadcasterId = broadcasterId,
            Game = game,
            GameConfigId = config.Id,
            Config = ToConfigView(config),
            JoinClosesAt = session.JoinClosesAt.Value,
            OverlayWidgetId = await overlayResolver.ResolveAsync(
                broadcasterId,
                manifest.OverlayWidgetKey,
                ct
            ),
            NextTickAt = manifest.TickInterval is TimeSpan tick ? now + tick : null,
        };

        LiveGameTransition opening = await game.OnStartAsync(BuildState(runtime), ct);
        await ApplyTransitionAsync(runtime, session, opening, ct);
        if (!runtime.Terminal)
            registry.TryRegister(runtime);

        await eventBus.PublishAsync(
            new LiveGameStartedEvent
            {
                BroadcasterId = broadcasterId,
                SessionId = session.Id,
                GameType = game.GameKey,
                StartedByUserId = command.StartedByUserId,
            },
            ct
        );
        return Result.Success(ToDto(session));
    }

    public async Task<Result> CancelAsync(
        Guid broadcasterId,
        Guid sessionId,
        CancellationToken ct = default
    )
    {
        GameSession? session = await db.GameSessions.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Id == sessionId && s.DeletedAt == null,
            ct
        );
        if (session is null)
            return Result.Failure("Session not found.", "NOT_FOUND");
        if (!NonTerminal.Contains(session.Status))
            return Result.Failure("Session is not active.", "SESSION_NOT_ACTIVE");

        if (
            registry.TryGet(broadcasterId, out LiveGameSessionRuntime? runtime)
            && runtime.SessionId == sessionId
        )
        {
            await runtime.Gate.WaitAsync(ct);
            try
            {
                if (!runtime.Terminal)
                    await CancelInternalAsync(runtime, session, "host_cancel", ct);
            }
            finally
            {
                runtime.Gate.Release();
            }
            return Result.Success();
        }

        // Not on this node's registry (restart leftovers): still refund + close the row, no frame to push.
        await games.RefundLiveGameAsync(broadcasterId, sessionId, ct);
        session.Status = GameSessionStatus.Cancelled;
        session.CancelReason = "host_cancel";
        session.ResolvedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishCancelledAsync(session, "host_cancel", ct);
        return Result.Success();
    }

    public async Task<Result<GameSessionDto>> GetActiveAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        GameSession? session = await db
            .GameSessions.AsNoTracking()
            .Where(s =>
                s.BroadcasterId == broadcasterId
                && s.DeletedAt == null
                && NonTerminal.Contains(s.Status)
            )
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return session is null
            ? Result.Failure<GameSessionDto>("No active live game session.", "NOT_FOUND")
            : Result.Success(ToDto(session));
    }

    public async Task<Result<PagedList<GameSessionDto>>> ListAsync(
        Guid broadcasterId,
        GameSessionFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<GameSession> query = db
            .GameSessions.AsNoTracking()
            .Where(s => s.BroadcasterId == broadcasterId && s.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(filter.GameType))
            query = query.Where(s => s.GameType == filter.GameType);
        if (
            !string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse(filter.Status, ignoreCase: true, out GameSessionStatus status)
        )
            query = query.Where(s => s.Status == status);

        int total = await query.CountAsync(ct);
        List<GameSession> rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<GameSessionDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    // ── Internal ops — driven by the chat listener and the runner (same scope contract as the REST ops) ──

    /// <summary>
    /// Routes one matched chat message into the active session (D6): first-time chatters are staked (when
    /// the manifest demands a fee — a failed stake silently skips the joiner) and added as participants;
    /// the input then runs the game's <c>OnInputAsync</c> under the session gate.
    /// </summary>
    public async Task HandleChatInputAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string displayName,
        string message,
        CancellationToken ct = default
    )
    {
        if (
            !registry.TryGet(broadcasterId, out LiveGameSessionRuntime? runtime) || runtime.Terminal
        )
            return;
        if (runtime.Phase is not (LiveGamePhase.Lobby or LiveGamePhase.Running))
            return;

        string[] tokens = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return;
        LiveGameManifest manifest = runtime.Game.Manifest;
        string? keyword = manifest.InputKeywords.FirstOrDefault(k =>
            string.Equals(k, tokens[0], StringComparison.OrdinalIgnoreCase)
        );
        if (keyword is null)
            return;

        await runtime.Gate.WaitAsync(ct);
        try
        {
            if (
                runtime.Terminal
                || runtime.Phase is not (LiveGamePhase.Lobby or LiveGamePhase.Running)
            )
                return;
            GameSession? session = await db.GameSessions.FirstOrDefaultAsync(
                s => s.Id == runtime.SessionId,
                ct
            );
            if (session is null)
                return;

            LiveGameParticipant? player = runtime.Participants.FirstOrDefault(p =>
                p.UserId == viewerUserId
            );
            if (player is null)
            {
                if (manifest.MaxPlayers > 0 && runtime.Participants.Count >= manifest.MaxPlayers)
                    return;

                Guid accountId = Guid.Empty;
                long stakeAmount = 0;
                if (manifest.RequiresEntryFee)
                {
                    stakeAmount = ResolveStake(tokens, runtime.Config);
                    Result<LiveGameStakeResult> staked = await games.StakeLiveGameEntryAsync(
                        broadcasterId,
                        new LiveGameStakeCommand(
                            runtime.SessionId,
                            runtime.GameConfigId,
                            viewerUserId,
                            stakeAmount
                        ),
                        ct
                    );
                    // A joiner who cannot pay is skipped silently — spamming per-viewer rejections
                    // would drown the room while a round runs.
                    if (staked.IsFailure)
                        return;
                    runtime.Stakes[viewerUserId] = staked.Value;
                    accountId = staked.Value.AccountId;
                }
                player = new LiveGameParticipant(viewerUserId, accountId, displayName, stakeAmount);
                runtime.Participants.Add(player);
            }

            LiveGameTransition transition = await runtime.Game.OnInputAsync(
                BuildState(runtime),
                new LiveGameInput(player, keyword, [.. tokens.Skip(1)], message),
                ct
            );
            bool maxReached =
                manifest.MaxPlayers > 0 && runtime.Participants.Count >= manifest.MaxPlayers;
            await ApplyTransitionAsync(
                runtime,
                session,
                maxReached ? transition with { Resolve = true } : transition,
                ct
            );
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    /// <summary>
    /// The runner's wall-clock entry: closes the lobby at <c>JoinClosesAt</c> (straight to resolution for
    /// tick-less games), and drives <c>OnTickAsync</c> at the manifest's interval.
    /// </summary>
    public async Task AdvanceClockAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        if (
            !registry.TryGet(broadcasterId, out LiveGameSessionRuntime? runtime) || runtime.Terminal
        )
            return;

        await runtime.Gate.WaitAsync(ct);
        try
        {
            if (runtime.Terminal)
                return;
            GameSession? session = await db.GameSessions.FirstOrDefaultAsync(
                s => s.Id == runtime.SessionId,
                ct
            );
            if (session is null)
                return;

            DateTime now = clock.GetUtcNow().UtcDateTime;
            LiveGameManifest manifest = runtime.Game.Manifest;

            if (runtime.Phase == LiveGamePhase.Lobby && now >= runtime.JoinClosesAt)
            {
                if (manifest.TickInterval is null)
                {
                    await ResolveAsync(runtime, session, ct);
                    return;
                }
                runtime.Phase = LiveGamePhase.Running;
                session.Status = GameSessionStatus.Running;
                await db.SaveChangesAsync(ct);
            }

            if (
                !runtime.Terminal
                && manifest.TickInterval is TimeSpan interval
                && runtime.NextTickAt is DateTime due
                && now >= due
            )
            {
                runtime.NextTickAt = now + interval;
                LiveGameTransition transition = await runtime.Game.OnTickAsync(
                    BuildState(runtime),
                    ct
                );
                await ApplyTransitionAsync(runtime, session, transition, ct);
            }
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    // ── The engine ↔ game loop plumbing ──

    private async Task ApplyTransitionAsync(
        LiveGameSessionRuntime runtime,
        GameSession session,
        LiveGameTransition transition,
        CancellationToken ct
    )
    {
        session.StateJson = JsonConvert.SerializeObject(runtime.Data);
        session.ParticipantCount = runtime.Participants.Count;
        await db.SaveChangesAsync(ct);

        if (transition.PushOverlay)
            await PushFrameAsync(runtime, transition.OverlayPayload, ct);
        if (transition.Resolve)
            await ResolveAsync(runtime, session, ct);
    }

    private async Task ResolveAsync(
        LiveGameSessionRuntime runtime,
        GameSession session,
        CancellationToken ct
    )
    {
        runtime.Phase = LiveGamePhase.Resolving;
        session.Status = GameSessionStatus.Resolving;
        await db.SaveChangesAsync(ct);

        LiveGameManifest manifest = runtime.Game.Manifest;
        if (runtime.Participants.Count < manifest.MinPlayers)
        {
            await CancelInternalAsync(runtime, session, "min_players_unmet", ct);
            return;
        }

        LiveGameResolution resolution = await runtime.Game.OnResolveAsync(BuildState(runtime), ct);
        List<LiveGameSettlementAward> awards = [];
        foreach (LiveGameAward award in resolution.Awards)
        {
            runtime.Stakes.TryGetValue(award.UserId, out LiveGameStakeResult? stake);
            awards.Add(
                new LiveGameSettlementAward(
                    award.UserId,
                    award.AccountId,
                    award.Stake,
                    award.Outcome,
                    award.Payout,
                    stake?.BetLedgerEntryId,
                    stake?.BetTenantPosition
                )
            );
        }

        int winnerCount = 0;
        long totalPaidOut = 0;
        Result<LiveGameSettlementResult> settled = await games.SettleLiveGameAsync(
            runtime.BroadcasterId,
            new LiveGameSettlement(
                runtime.SessionId,
                runtime.GameConfigId,
                session.GameType,
                awards
            ),
            ct
        );
        if (settled.IsSuccess)
        {
            winnerCount = settled.Value.WinnerCount;
            totalPaidOut = settled.Value.TotalPaidOut;
        }
        else
            logger.LogWarning(
                "Live game {SessionId} settlement failed: {Error} ({Code})",
                runtime.SessionId,
                settled.ErrorMessage,
                settled.ErrorCode
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        session.Status = GameSessionStatus.Settled;
        session.ResolvedAt = now;
        session.StateJson = JsonConvert.SerializeObject(runtime.Data);
        session.OutcomeJson = JsonConvert.SerializeObject(
            new Dictionary<string, object?>
            {
                ["participants"] = runtime.Participants.Count,
                ["winners"] = winnerCount,
                ["totalPaidOut"] = totalPaidOut,
            }
        );
        await db.SaveChangesAsync(ct);

        runtime.Terminal = true;
        registry.Remove(runtime.BroadcasterId, runtime.SessionId);

        await eventBus.PublishAsync(
            new LiveGameResolvedEvent
            {
                BroadcasterId = runtime.BroadcasterId,
                SessionId = runtime.SessionId,
                GameType = session.GameType,
                ParticipantCount = runtime.Participants.Count,
                WinnerCount = winnerCount,
                TotalPaidOut = totalPaidOut,
            },
            ct
        );
        await PushFrameAsync(runtime, resolution.FinalOverlayPayload, ct);
    }

    private async Task CancelInternalAsync(
        LiveGameSessionRuntime runtime,
        GameSession session,
        string reason,
        CancellationToken ct
    )
    {
        await games.RefundLiveGameAsync(runtime.BroadcasterId, runtime.SessionId, ct);
        session.Status = GameSessionStatus.Cancelled;
        session.CancelReason = reason;
        session.ResolvedAt = clock.GetUtcNow().UtcDateTime;
        session.StateJson = JsonConvert.SerializeObject(runtime.Data);
        await db.SaveChangesAsync(ct);

        runtime.Terminal = true;
        registry.Remove(runtime.BroadcasterId, runtime.SessionId);
        await PublishCancelledAsync(session, reason, ct);
        await PushFrameAsync(
            runtime,
            new Dictionary<string, object?> { ["cancelled"] = true, ["reason"] = reason },
            ct
        );
    }

    private Task PublishCancelledAsync(GameSession session, string reason, CancellationToken ct) =>
        eventBus.PublishAsync(
            new LiveGameCancelledEvent
            {
                BroadcasterId = session.BroadcasterId,
                SessionId = session.Id,
                GameType = session.GameType,
                Reason = reason,
            },
            ct
        );

    /// <summary>
    /// Pushes one <c>game.&lt;phase&gt;</c> frame to the session's overlay widget — silently skipped when the
    /// channel has not installed the game's widget (the round still runs; only the overlay stays dark).
    /// </summary>
    private async Task PushFrameAsync(
        LiveGameSessionRuntime runtime,
        object? payload,
        CancellationToken ct
    )
    {
        if (runtime.OverlayWidgetId is not Guid widgetId)
            return;
        string phase = runtime.Terminal
            ? "resolved"
            : runtime.Phase switch
            {
                LiveGamePhase.Lobby => "lobby",
                LiveGamePhase.Running => "running",
                _ => "resolved",
            };
        await overlay.SendWidgetEventAsync(
            runtime.BroadcasterId,
            widgetId,
            $"game.{phase}",
            payload,
            ct
        );
    }

    private LiveGameState BuildState(LiveGameSessionRuntime runtime) =>
        new()
        {
            SessionId = runtime.SessionId,
            BroadcasterId = runtime.BroadcasterId,
            Config = runtime.Config,
            Participants = runtime.Participants,
            Phase = runtime.Phase,
            Data = runtime.Data,
            Random = new GameRandomAdapter(randomizer),
        };

    /// <summary>The first numeric token clamped to the config's bet bounds; absent → <c>MinBet</c> (floor 1).</summary>
    private static long ResolveStake(string[] tokens, GameConfigView config)
    {
        long min = config.MinBet ?? 1;
        long max = config.MaxBet ?? long.MaxValue;
        foreach (string token in tokens.Skip(1))
            if (long.TryParse(token, out long parsed))
                return Math.Clamp(parsed, min, max);
        return min;
    }

    private static GameConfigView ToConfigView(GameConfig config) =>
        new(
            config.MinBet,
            config.MaxBet,
            config.PayoutMultiplier,
            config.ConfigJson is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, object?>>(config.ConfigJson)
        );

    internal static GameSessionDto ToDto(GameSession s) =>
        new(
            s.Id,
            s.GameType,
            s.Status.ToString(),
            s.ParticipantCount,
            s.StartedAt,
            s.JoinClosesAt,
            s.ResolvedAt,
            s.StateJson is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, object?>>(s.StateJson),
            s.OutcomeJson is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, object?>>(s.OutcomeJson)
        );
}
