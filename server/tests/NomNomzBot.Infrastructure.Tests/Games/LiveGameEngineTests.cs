// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Proves the generic live-game engine (live-games.md §3/§4) against the real SQLite ledger with a FAKE
/// drop-in game: the full lobby→settled lifecycle (stakes taken on join, winners credited, session-linked
/// <c>GamePlay</c> rows, both domain events, overlay frames per phase); the D7 single-session guard; the
/// min-players cancel with a full refund; the drop-in/duplicate-key catalog contract; and the startup sweep
/// that cancels + refunds orphaned rows exactly once.
/// </summary>
public sealed class LiveGameEngineTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000d3");
    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-0000000000d9");

    private sealed class FixedRandomizer : IGameRandomizer
    {
        public double NextUnitInterval() => 0.5;
    }

    /// <summary>A drop-in game: records joins in its Data bag; on resolve every participant wins 2× stake.</summary>
    private sealed class FakeGame : ILiveGame
    {
        public string GameKey { get; init; } = "fake_game";
        public LiveGameManifest Manifest { get; init; } =
            new(
                "Fake Game",
                ["!join"],
                "fake_widget",
                MinPlayers: 1,
                MaxPlayers: 0,
                LobbyWindow: TimeSpan.FromSeconds(30),
                TickInterval: null,
                RequiresEntryFee: true
            );

        public Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct)
        {
            state.Data["opened"] = true;
            return Task.FromResult(LiveGameTransition.Push(new { phase = "open" }));
        }

        public Task<LiveGameTransition> OnInputAsync(
            LiveGameState state,
            LiveGameInput input,
            CancellationToken ct
        )
        {
            state.Data[$"joined:{input.Player.DisplayName}"] = input.Keyword;
            return Task.FromResult(LiveGameTransition.Continue());
        }

        public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct) =>
            Task.FromResult(LiveGameTransition.Continue());

        public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct)
        {
            List<LiveGameAward> awards =
            [
                .. state.Participants.Select(p => new LiveGameAward(
                    p.UserId,
                    p.AccountId,
                    p.Stake,
                    GameOutcome.Win,
                    p.Stake * 2
                )),
            ];
            return Task.FromResult(new LiveGameResolution(awards, new { done = true }));
        }
    }

    /// <summary>A game whose tick always throws — used to prove the runner's force-cancel-after-N-failures path.</summary>
    private sealed class ThrowingTickGame : ILiveGame
    {
        public string GameKey => "throwing_game";
        public LiveGameManifest Manifest { get; init; } =
            new(
                "Throwing Game",
                ["!join"],
                "fake_widget",
                MinPlayers: 1,
                MaxPlayers: 0,
                LobbyWindow: TimeSpan.FromSeconds(30),
                TickInterval: TimeSpan.FromSeconds(1),
                RequiresEntryFee: true
            );

        public Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct) =>
            Task.FromResult(LiveGameTransition.Push(new { phase = "open" }));

        public Task<LiveGameTransition> OnInputAsync(
            LiveGameState state,
            LiveGameInput input,
            CancellationToken ct
        ) => Task.FromResult(LiveGameTransition.Continue());

        public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct) =>
            throw new InvalidOperationException("tick boom");

        public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct) =>
            Task.FromResult(new LiveGameResolution([], new { done = true }));
    }

    /// <summary>Records every chat send — proves the "insufficient funds" feedback to a rejected joiner.</summary>
    private sealed class RecordingChatProvider : IChatProvider
    {
        public List<string> SentMessages { get; } = [];

        public Task<bool> SendMessageAsync(
            Guid broadcasterId,
            string message,
            CancellationToken cancellationToken = default
        )
        {
            SentMessages.Add(message);
            return Task.FromResult(true);
        }

        public Task<bool> SendReplyAsync(
            Guid broadcasterId,
            string replyToMessageId,
            string message,
            CancellationToken cancellationToken = default
        )
        {
            SentMessages.Add(message);
            return Task.FromResult(true);
        }

        public Task TimeoutUserAsync(
            Guid broadcasterId,
            string userId,
            int durationSeconds,
            string? reason = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task BanUserAsync(
            Guid broadcasterId,
            string userId,
            string? reason = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task UnbanUserAsync(
            Guid broadcasterId,
            string userId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task DeleteMessageAsync(
            Guid broadcasterId,
            string messageId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    /// <summary>Forwards every call to a real <see cref="IGameService"/> except settlement, which always fails —
    /// proves the engine refunds instead of losing stakes when settlement cannot complete.</summary>
    private sealed class SettleFailingGameService(IGameService inner) : IGameService
    {
        public Task<Result<IReadOnlyList<GameConfigDto>>> ListGamesAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => inner.ListGamesAsync(broadcasterId, ct);

        public Task<Result<GameConfigDto>> UpsertGameAsync(
            Guid broadcasterId,
            UpsertGameConfigRequest request,
            CancellationToken ct = default
        ) => inner.UpsertGameAsync(broadcasterId, request, ct);

        public Task<Result<GamePlayResultDto>> PlayAsync(
            Guid broadcasterId,
            PlayGameRequest request,
            CancellationToken ct = default
        ) => inner.PlayAsync(broadcasterId, request, ct);

        public Task<Result<PagedList<GamePlayDto>>> GetGameHistoryAsync(
            Guid broadcasterId,
            GameHistoryFilter filter,
            PaginationParams pagination,
            CancellationToken ct = default
        ) => inner.GetGameHistoryAsync(broadcasterId, filter, pagination, ct);

        public Task<Result<LiveGameStakeResult>> StakeLiveGameEntryAsync(
            Guid broadcasterId,
            LiveGameStakeCommand command,
            CancellationToken ct = default
        ) => inner.StakeLiveGameEntryAsync(broadcasterId, command, ct);

        public Task<Result<LiveGameSettlementResult>> SettleLiveGameAsync(
            Guid broadcasterId,
            LiveGameSettlement settlement,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Failure<LiveGameSettlementResult>("Forced settlement failure.", "FORCED")
            );

        public Task<Result> RefundLiveGameAsync(
            Guid broadcasterId,
            Guid sessionId,
            CancellationToken ct = default
        ) => inner.RefundLiveGameAsync(broadcasterId, sessionId, ct);
    }

    private sealed class RecordingNotifier : IWidgetEventNotifier
    {
        public List<(Guid WidgetId, string EventType, object? Data)> Sent { get; } = [];

        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        )
        {
            Sent.Add((widgetId, eventType, data));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOverlayResolver(Guid? widgetId) : ILiveGameOverlayResolver
    {
        public Task<Guid?> ResolveAsync(
            Guid broadcasterId,
            string overlayWidgetKey,
            CancellationToken ct = default
        ) => Task.FromResult(widgetId);
    }

    private sealed record Harness(
        LiveGameEngine Engine,
        EventStoreTestDbContext Db,
        GameService Games,
        RecordingEventBus Bus,
        RecordingNotifier Overlay,
        LiveGameSessionRegistry Registry,
        FakeTimeProvider Clock,
        RecordingChatProvider Chat
    );

    private static Harness New(SqliteTestDatabase database, params ILiveGame[] games)
    {
        FakeTimeProvider clock = new(new(2026, 7, 17, 14, 0, 0, TimeSpan.Zero));
        EventStoreTestDbContext db = database.NewContext();
        if (!db.CurrencyConfigs.Any(c => c.BroadcasterId == Channel))
        {
            db.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Channel,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 100,
                }
            );
            db.SaveChanges();
        }
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            clock
        );
        IAgeConsentService age = Substitute.For<IAgeConsentService>();
        age.HasGrantedAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        GameService gameService = new(db, accounts, age, new FixedRandomizer(), bus, clock);

        RecordingNotifier overlay = new();
        LiveGameSessionRegistry registry = new();
        RecordingChatProvider chat = new();
        LiveGameEngine engine = new(
            db,
            gameService,
            chat,
            overlay,
            new LiveGameCatalog(games),
            new FixedOverlayResolver(WidgetId),
            registry,
            new FixedRandomizer(),
            bus,
            clock,
            NullLogger<LiveGameEngine>.Instance
        );
        return new(engine, db, gameService, bus, overlay, registry, clock, chat);
    }

    private static Guid SeedConfig(
        EventStoreTestDbContext db,
        string gameType = "fake_game",
        long maxBet = 100
    )
    {
        GameConfig config = new()
        {
            BroadcasterId = Channel,
            GameType = gameType,
            Category = GameCategory.Minigame,
            IsEnabled = true,
            MinBet = 10,
            MaxBet = maxBet,
            Permission = "Everyone",
        };
        db.GameConfigs.Add(config);
        db.SaveChanges();
        return config.Id;
    }

    [Fact]
    public async Task The_full_lifecycle_stakes_joiners_settles_winners_and_pushes_frames()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new FakeGame());
        SeedConfig(h.Db);

        Result<GameSessionDto> started = await h.Engine.StartAsync(
            Channel,
            new("fake_game", PlayerA)
        );
        started.IsSuccess.Should().BeTrue(started.ErrorMessage);
        started.Value.Status.Should().Be("Lobby");
        h.Bus.Published.OfType<LiveGameStartedEvent>().Should().ContainSingle();
        h.Overlay.Sent.Should().ContainSingle(f => f.EventType == "game.lobby");

        // Two chatters join: A bets 40, B sends no amount → MinBet (10).
        await h.Engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!join 40");
        await h.Engine.HandleChatInputAsync(Channel, PlayerB, "Bob", "!join");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(60, "the 40 stake was debited on join");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(90, "no numeric arg falls back to MinBet");

        // The lobby closes: a tick-less game resolves straight away.
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        await h.Engine.AdvanceClockAsync(Channel);

        GameSession session = await h.Db.GameSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(GameSessionStatus.Settled);
        session.ParticipantCount.Should().Be(2);
        session.ResolvedAt.Should().NotBeNull();
        session.OutcomeJson.Should().Contain("\"winners\":2");

        // Every participant won 2× their stake, recorded as session-linked plays.
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(140);
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(110);
        List<GamePlay> plays = await h
            .Db.GamePlays.Where(p => p.GameSessionId == session.Id)
            .ToListAsync();
        plays.Should().HaveCount(2);
        plays.Should().OnlyContain(p => p.Outcome == GameOutcome.Win);

        LiveGameResolvedEvent resolved = h
            .Bus.Published.OfType<LiveGameResolvedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        resolved.ParticipantCount.Should().Be(2);
        resolved.WinnerCount.Should().Be(2);
        resolved.TotalPaidOut.Should().Be(100);

        h.Overlay.Sent.Should().Contain(f => f.EventType == "game.resolved");
        h.Registry.TryGet(Channel, out _).Should().BeFalse("a settled session leaves the registry");
    }

    [Fact]
    public async Task A_second_start_while_one_is_active_fails_SESSION_ALREADY_ACTIVE()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new FakeGame());
        SeedConfig(h.Db);
        (await h.Engine.StartAsync(Channel, new("fake_game", null))).IsSuccess.Should().BeTrue();

        Result<GameSessionDto> second = await h.Engine.StartAsync(Channel, new("fake_game", null));

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("SESSION_ALREADY_ACTIVE");
        (await h.Db.GameSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Under_min_players_the_round_cancels_and_refunds_every_stake()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        FakeGame game = new()
        {
            Manifest = new(
                "Fake Game",
                ["!join"],
                "fake_widget",
                MinPlayers: 2,
                MaxPlayers: 0,
                LobbyWindow: TimeSpan.FromSeconds(30),
                TickInterval: null,
                RequiresEntryFee: true
            ),
        };
        Harness h = New(database, game);
        SeedConfig(h.Db);
        await h.Engine.StartAsync(Channel, new("fake_game", null));
        await h.Engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!join 40");

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        await h.Engine.AdvanceClockAsync(Channel);

        GameSession session = await h.Db.GameSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(GameSessionStatus.Cancelled);
        session.CancelReason.Should().Be("min_players_unmet");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(100, "the stake was fully refunded");
        (
            await h.Db.CurrencyLedgerEntries.CountAsync(e =>
                e.EntryType == CurrencyEntryType.RefundGame
            )
        )
            .Should()
            .Be(1);
        h.Bus.Published.OfType<LiveGameCancelledEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "min_players_unmet");
        (await h.Db.GamePlays.CountAsync()).Should().Be(0, "nobody was settled");
    }

    [Fact]
    public async Task A_second_game_is_a_drop_in_and_a_duplicate_key_fails_the_catalog_build()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        FakeGame second = new()
        {
            GameKey = "other_game",
            Manifest = new(
                "Other",
                ["!other"],
                "other_widget",
                MinPlayers: 1,
                MaxPlayers: 0,
                LobbyWindow: TimeSpan.FromSeconds(10),
                TickInterval: null,
                RequiresEntryFee: false
            ),
        };
        Harness h = New(database, new FakeGame(), second);
        SeedConfig(h.Db, "other_game");

        // The second game runs with ZERO engine edits — discovered, started, and settled generically.
        Result<GameSessionDto> started = await h.Engine.StartAsync(
            Channel,
            new("other_game", null)
        );
        started.IsSuccess.Should().BeTrue(started.ErrorMessage);

        Action duplicate = () => _ = new LiveGameCatalog([new FakeGame(), new FakeGame()]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*fake_game*");
    }

    [Fact]
    public async Task The_startup_sweep_cancels_and_refunds_an_orphaned_session_exactly_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new FakeGame());
        Guid configId = SeedConfig(h.Db);

        // A crash left a lobby-phase row with one staked participant and no in-memory state.
        GameSession orphan = new()
        {
            BroadcasterId = Channel,
            GameConfigId = configId,
            GameType = "fake_game",
            Status = GameSessionStatus.Lobby,
            StartedAt = h.Clock.GetUtcNow().UtcDateTime,
        };
        h.Db.GameSessions.Add(orphan);
        await h.Db.SaveChangesAsync(CancellationToken.None);
        (await h.Games.StakeLiveGameEntryAsync(Channel, new(orphan.Id, configId, PlayerA, 40)))
            .IsSuccess.Should()
            .BeTrue();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(h.Db);
        services.AddSingleton<IGameService>(h.Games);
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(h.Bus);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        guard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        LiveGameRunner runner = new(
            scopeFactory,
            h.Registry,
            guard,
            h.Clock,
            NullLogger<LiveGameRunner>.Instance
        );

        await runner.SweepOrphanedSessionsAsync(CancellationToken.None);
        await runner.SweepOrphanedSessionsAsync(CancellationToken.None);

        GameSession swept = await h
            .Db.GameSessions.AsNoTracking()
            .SingleAsync(s => s.Id == orphan.Id);
        swept.Status.Should().Be(GameSessionStatus.Cancelled);
        swept.CancelReason.Should().Be("startup_sweep");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(100, "the orphaned stake was refunded");
        (
            await h.Db.CurrencyLedgerEntries.CountAsync(e =>
                e.EntryType == CurrencyEntryType.RefundGame
            )
        )
            .Should()
            .Be(1, "the sweep is idempotent — a second run reverses nothing more");
        h.Bus.Published.OfType<LiveGameCancelledEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "startup_sweep");
    }

    [Fact]
    public async Task A_forced_settle_failure_cancels_and_refunds_every_stake_and_a_retry_does_not_double_pay()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new FakeGame());
        SeedConfig(h.Db);

        // A second engine over the SAME components, but its game service always fails settlement — proves
        // the engine refunds instead of marking the round Settled with un-paid stakes.
        LiveGameEngine failingEngine = new(
            h.Db,
            new SettleFailingGameService(h.Games),
            h.Chat,
            h.Overlay,
            new LiveGameCatalog([new FakeGame()]),
            new FixedOverlayResolver(WidgetId),
            h.Registry,
            new FixedRandomizer(),
            h.Bus,
            h.Clock,
            NullLogger<LiveGameEngine>.Instance
        );

        (await failingEngine.StartAsync(Channel, new("fake_game", PlayerA)))
            .IsSuccess.Should()
            .BeTrue();
        await failingEngine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!join 40");
        await failingEngine.HandleChatInputAsync(Channel, PlayerB, "Bob", "!join 30");

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        await failingEngine.AdvanceClockAsync(Channel);

        GameSession session = await h.Db.GameSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(GameSessionStatus.Cancelled);
        session.CancelReason.Should().Be("settlement_failed");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(100, "the forced settle failure refunded Alice's stake in full");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(100, "the forced settle failure refunded Bob's stake in full");
        (await h.Db.GamePlays.CountAsync()).Should().Be(0, "nobody was settled");
        h.Bus.Published.OfType<LiveGameCancelledEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "settlement_failed");

        // A retried refund (e.g. a later startup sweep) must not double-pay.
        (await h.Games.RefundLiveGameAsync(Channel, session.Id))
            .IsSuccess.Should()
            .BeTrue();
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(100);
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(100);
        (
            await h.Db.CurrencyLedgerEntries.CountAsync(e =>
                e.EntryType == CurrencyEntryType.RefundGame
            )
        )
            .Should()
            .Be(2, "one refund per stake — the retry reversed nothing more");
    }

    [Fact]
    public async Task A_joiner_who_cannot_pay_is_told_why_and_is_not_added_as_a_participant()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new FakeGame());
        // A wide bet range so a large ask isn't clamped down to something the starting balance can afford.
        SeedConfig(h.Db, maxBet: 1000);
        await h.Engine.StartAsync(Channel, new("fake_game", null));

        // The starting balance is 100 (harness default) — 500 must be rejected as insufficient funds.
        await h.Engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!join 500");

        h.Chat.SentMessages.Should()
            .ContainSingle(m => m.Contains("Alice") && m.Contains("Insufficient funds"));
        h.Registry.TryGet(Channel, out LiveGameSessionRuntime? runtime).Should().BeTrue();
        runtime!
            .Participants.Should()
            .BeEmpty("the rejected joiner must not silently be in the game");
        (await h.Db.CurrencyAccounts.CountAsync(a => a.ViewerUserId == PlayerA))
            .Should()
            .Be(0, "the failed stake attempt rolled back — it moved and created nothing");
    }

    [Fact]
    public async Task A_runtime_that_fails_N_consecutive_ticks_force_cancels_and_refunds_all_stakes()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = New(database, new ThrowingTickGame());
        SeedConfig(h.Db, "throwing_game");

        (await h.Engine.StartAsync(Channel, new("throwing_game", null)))
            .IsSuccess.Should()
            .BeTrue();
        await h.Engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!join 40");

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(h.Db);
        services.AddSingleton<IGameService>(h.Games);
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(h.Bus);
        services.AddSingleton<IWidgetEventNotifier>(h.Overlay);
        services.AddSingleton<ILiveGameCatalog>(new LiveGameCatalog([new ThrowingTickGame()]));
        services.AddSingleton<ILiveGameOverlayResolver>(new FixedOverlayResolver(WidgetId));
        services.AddSingleton(h.Registry);
        services.AddSingleton<IGameRandomizer>(new FixedRandomizer());
        services.AddSingleton<TimeProvider>(h.Clock);
        services.AddSingleton<IChatProvider>(h.Chat);
        services.AddSingleton<ILogger<LiveGameEngine>>(NullLogger<LiveGameEngine>.Instance);
        services.AddScoped<LiveGameEngine>();
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        LiveGameRunner runner = new(
            scopeFactory,
            h.Registry,
            Substitute.For<IRunOnceGuard>(),
            h.Clock,
            NullLogger<LiveGameRunner>.Instance
        );

        h.Registry.TryGet(Channel, out LiveGameSessionRuntime? runtime).Should().BeTrue();

        // The lobby closes and every subsequent tick throws — three consecutive failures must force-cancel.
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        for (int i = 0; i < LiveGameRunner.MaxConsecutiveTickFailures; i++)
        {
            await runner.AdvanceRuntimeAsync(runtime!, CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        GameSession session = await h.Db.GameSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(GameSessionStatus.Cancelled);
        session.CancelReason.Should().Be("runner_stuck");
        (await h.Db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(100, "the stuck game's stake was refunded in full");
        h.Registry.TryGet(Channel, out _)
            .Should()
            .BeFalse("a force-cancelled session leaves the registry");
        h.Bus.Published.OfType<LiveGameCancelledEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "runner_stuck");
    }
}
